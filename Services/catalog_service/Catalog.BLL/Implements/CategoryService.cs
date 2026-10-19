using System.Data;
using System.Globalization;
using System.Text;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Persistence.Interfaces;

namespace Catalog.BLL.Implements;

public sealed class CategoryService(
    ICategoryRepository categories,
    IItemRepository items,
    IUnitOfWork unitOfWork,
    ICatalogAccess access,
    IOptions<CatalogManagementSettings> settings) : ICategoryService
{
    public async Task<ResponseDTO> GetAll(
        CategoryQueryDTO query,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (query is null || !query.IsValid())
        {
            return new ResponseDTO(
                "Invalid pagination or search query.",
                400);
        }

        var result = await categories.GetPageAsync(
            query,
            cancellationToken);

        return new ResponseDTO(
            "Categories retrieved successfully.",
            200,
            true,
            result);
    }

    public async Task<ResponseDTO> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        var category = await categories.GetByIdAsync(
            id,
            cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return new ResponseDTO(
            "Category retrieved successfully.",
            200,
            true,
            Map(category));
    }

    public async Task<ResponseDTO> Create(
        CreateCategoryDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        var name = NormalizeName(request?.CategoryName);

        if (!IsValidName(name))
        {
            return InvalidName();
        }

        var code = CreateCode(name);
        if (code.Length is < 1 or > 50)
        {
            return new ResponseDTO(
                "The category name must produce a slug between 1 and 50 characters. Choose a different name.",
                400);
        }

        return await WriteAsync(async () =>
        {
            if (await categories.NameExistsAsync(
                name,
                Guid.Empty,
                cancellationToken))
            {
                return DuplicateName();
            }

            if (await categories.CodeExistsAsync(code, cancellationToken))
            {
                return new ResponseDTO(
                    "The generated category code already exists. Choose a different name.",
                    409);
            }

            var category = new Category
            {
                CategoryId = Guid.NewGuid(),
                CategoryCode = code,
                CategoryName = name,
                IsActive = true
            };

            categories.Create(category);

            return new ResponseDTO(
                "Category created successfully.",
                201,
                true,
                Map(category));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(
        Guid id,
        UpdateCategoryDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        var name = NormalizeName(request?.CategoryName);

        if (!IsValidName(name))
        {
            return InvalidName();
        }

        return await WriteAsync(async () =>
        {
            var category = await categories.GetByIdAsync(
                id,
                cancellationToken);

            if (category is null)
            {
                return NotFound();
            }

            if (await categories.NameExistsAsync(
                name,
                id,
                cancellationToken))
            {
                return DuplicateName();
            }

            category.CategoryName = name;

            return new ResponseDTO(
                "Category updated successfully.",
                200,
                true,
                Map(category));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> SetStatus(
        Guid id,
        SetCategoryStatusDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        if (request?.IsActive is not bool isActive)
        {
            return new ResponseDTO(
                "The isActive field is required.",
                400);
        }

        return await WriteAsync(async () =>
        {
            var category = await categories.GetByIdAsync(
                id,
                cancellationToken);

            if (category is null)
            {
                return NotFound();
            }

            if (category.IsActive == isActive)
            {
                return new ResponseDTO(
                    "The category already has the requested status.",
                    200,
                    true,
                    Map(category));
            }

            if (!isActive && await items.HasSellingItemsAsync(
                id,
                settings.Value.NonSellingItemAvailabilities,
                cancellationToken))
            {
                return new ResponseDTO(
                    "Cannot deactivate a category that contains items still offered for sale.",
                    409);
            }

            category.IsActive = isActive;

            return new ResponseDTO(
                "Category status updated successfully.",
                200,
                true,
                Map(category));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        return await WriteAsync(async () =>
        {
            var category = await categories.GetByIdAsync(
                id,
                cancellationToken);

            if (category is null)
            {
                return NotFound();
            }

            if (await categories.HasItemsAsync(
                id,
                cancellationToken))
            {
                return new ResponseDTO(
                    "Cannot delete a category that contains items.",
                    409);
            }

            categories.Delete(category);

            return new ResponseDTO(
                "Category deleted successfully.",
                200,
                true);
        }, cancellationToken);
    }

    private async Task<ResponseDTO> WriteAsync(
        Func<Task<ResponseDTO>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                action,
                IsolationLevel.Serializable,
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && sqlException.Number is 2601 or 2627)
        {
            return new ResponseDTO(
                "The category code or name already exists.",
                409);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && sqlException.Number == 547)
        {
            return new ResponseDTO(
                "Related data has changed. Reload and try again.",
                409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ResponseDTO(
                "The data has changed. Reload and try again.",
                409);
        }
        catch (SqlException exception)
            when (exception.Number == 1205)
        {
            return new ResponseDTO(
                "A concurrent operation was detected. Try again.",
                409);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && sqlException.Number == 1205)
        {
            return new ResponseDTO(
                "A concurrent operation was detected. Try again.",
                409);
        }
    }

    private static string CreateCode(string name)
    {
        var builder = new StringBuilder();
        var separatorPending = false;

        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var value = char.ToLowerInvariant(character);
            if (value == 'đ')
            {
                value = 'd';
            }

            if (!char.IsAsciiLetterOrDigit(value))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append('-');
                separatorPending = false;
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    private static string NormalizeName(string? name)
    {
        return (name ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormC);
    }

    private static bool IsValidName(string name)
    {
        return name.Length is >= 1 and <= 255
            && !name.Any(char.IsControl);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Invalid category ID.", 400);
    }

    private static ResponseDTO InvalidName()
    {
        return new ResponseDTO(
            "The category name is required, must not exceed 255 characters, and must not contain control characters.",
            400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Category not found.", 404);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("The category name already exists.", 409);
    }

    private static CategoryDTO Map(Category category)
    {
        return new CategoryDTO
        {
            CategoryId = category.CategoryId,
            CategoryCode = category.CategoryCode,
            CategoryName = category.CategoryName,
            IsActive = category.IsActive
        };
    }
}
