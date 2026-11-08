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
                "Phân trang hoặc truy vấn tìm kiếm không hợp lệ.",
                400);
        }

        var result = await categories.GetPageAsync(
            query,
            cancellationToken);

        return new ResponseDTO(
            "Lấy danh sách danh mục thành công.",
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
            "Lấy thông tin danh mục thành công.",
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
                "Tên danh mục phải tạo ra slug từ 1 đến 50 ký tự. Vui lòng chọn tên khác.",
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
                    "Mã danh mục được tạo tự động đã tồn tại. Vui lòng chọn tên khác.",
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
                "Tạo danh mục thành công.",
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
                "Cập nhật danh mục thành công.",
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
                "Trường isActive là bắt buộc.",
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
                    "Danh mục đã ở trạng thái yêu cầu.",
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
                    "Không thể ngừng kích hoạt danh mục đang chứa các món ăn vẫn mở bán.",
                    409);
            }

            category.IsActive = isActive;

            return new ResponseDTO(
                "Cập nhật trạng thái danh mục thành công.",
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
                    "Không thể xóa danh mục đang chứa món ăn.",
                    409);
            }

            categories.Delete(category);

            return new ResponseDTO(
                "Xóa danh mục thành công.",
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
                "Mã hoặc tên danh mục đã tồn tại.",
                409);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && sqlException.Number == 547)
        {
            return new ResponseDTO(
                "Dữ liệu liên quan đã thay đổi. Vui lòng tải lại và thử lại.",
                409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ResponseDTO(
                "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",
                409);
        }
        catch (SqlException exception)
            when (exception.Number == 1205)
        {
            return new ResponseDTO(
                "Phát hiện thao tác đồng thời. Vui lòng thử lại.",
                409);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && sqlException.Number == 1205)
        {
            return new ResponseDTO(
                "Phát hiện thao tác đồng thời. Vui lòng thử lại.",
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
        return new ResponseDTO("Mã danh mục không hợp lệ.", 400);
    }

    private static ResponseDTO InvalidName()
    {
        return new ResponseDTO(
            "Tên danh mục là bắt buộc, không được vượt quá 255 ký tự và không chứa ký tự điều khiển.",
            400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Không tìm thấy danh mục.", 404);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("Tên danh mục đã tồn tại.", 409);
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
