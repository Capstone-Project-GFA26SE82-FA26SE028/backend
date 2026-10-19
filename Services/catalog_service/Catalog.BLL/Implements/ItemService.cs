using System.Text;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Exceptions;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.BLL.Implements;

public sealed class ItemService(
    IItemRepository items,
    ICategoryRepository categories,
    IItemTransaction transaction,
    ICatalogAccess access,
    IOptions<ItemManagementSettings> settings,
    IOptions<CatalogManagementSettings> catalogSettings,
    ILogger<ItemService> logger) : IItemService
{
    public async Task<ResponseDTO> GetAll(ItemQueryDTO query, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (query is null || !query.IsValid())
        {
            return new ResponseDTO("Invalid pagination, category ID, or item filter.", 400);
        }

        if (query.CategoryId.HasValue
            && await categories.GetByIdAsync(query.CategoryId.Value, cancellationToken) is null)
        {
            return CategoryNotFound();
        }

        var page = await items.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Items retrieved successfully.", 200, true,
            new PagedResult<ItemDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid item ID.", 400);
        }

        var item = await items.GetByIdAsync(id, cancellationToken);
        return item is null ? ItemNotFound() : new ResponseDTO("Item retrieved successfully.", 200, true, Map(item));
    }

    public async Task<ResponseDTO> Create(CreateItemDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        var name = NormalizeName(request?.ItemName);
        var description = NormalizeOptional(request?.Description);
        var imageUrl = NormalizeOptional(request?.ImageUrl);
        var validation = ValidateFields(request?.CategoryId ?? Guid.Empty, name, description, imageUrl);
        if (validation is not null)
        {
            return validation;
        }

        var code = ItemCodeGenerator.Create(name);
        if (code.Length is < 1 or > 50)
        {
            return new ResponseDTO("The item name must produce a slug between 1 and 50 characters.", 400);
        }

        var initialAvailability = settings.Value.InitialAvailability;
        if (string.IsNullOrWhiteSpace(initialAvailability)
            || !catalogSettings.Value.NonSellingItemAvailabilities.Contains(initialAvailability, StringComparer.Ordinal))
        {
            logger.LogError("Configure ItemManagement:InitialAvailability with a value from CatalogManagement:NonSellingItemAvailabilities.");
            return new ResponseDTO("Item creation is unavailable until the initial non-selling status is configured.", 503);
        }

        return await WriteAsync(async () =>
        {
            var category = await categories.GetByIdAsync(request!.CategoryId, cancellationToken);
            if (category is null)
            {
                return (CategoryNotFound(), null);
            }

            if (!category.IsActive)
            {
                return (InactiveCategory(), null);
            }

            if (await items.NameExistsAsync(category.CategoryId, name, Guid.Empty, cancellationToken))
            {
                return (DuplicateName(), null);
            }

            if (await items.CodeExistsAsync(code, cancellationToken))
            {
                return (new ResponseDTO("The generated item code already exists. Choose a different name.", 409), null);
            }

            var item = new Item
            {
                ItemId = Guid.NewGuid(),
                CategoryId = category.CategoryId,
                Category = category,
                ItemCode = code,
                ItemName = name,
                Description = description,
                ImageUrl = imageUrl,
                Availability = initialAvailability,
                KitchenStationRef = null
            };
            items.Create(item);
            return (new ResponseDTO("Item created successfully.", 201, true), item);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdateItemDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid item ID.", 400);
        }

        var name = NormalizeName(request?.ItemName);
        var description = NormalizeOptional(request?.Description);
        var imageUrl = NormalizeOptional(request?.ImageUrl);
        var validation = ValidateFields(request?.CategoryId ?? Guid.Empty, name, description, imageUrl);
        if (validation is not null)
        {
            return validation;
        }

        if (!TryParseVersion(request?.Version, out var version))
        {
            return new ResponseDTO("Version is required and must be a Base64-encoded SQL Server rowversion.", 400);
        }

        return await WriteAsync(async () =>
        {
            var item = await items.GetByIdAsync(id, cancellationToken);
            if (item is null)
            {
                return (ItemNotFound(), null);
            }

            if (!item.Version.SequenceEqual(version))
            {
                return (ConcurrentUpdate(), null);
            }

            var category = await categories.GetByIdAsync(request!.CategoryId, cancellationToken);
            if (category is null)
            {
                return (CategoryNotFound(), null);
            }

            if (item.CategoryId != category.CategoryId && !category.IsActive)
            {
                return (InactiveCategory(), null);
            }

            if (await items.NameExistsAsync(category.CategoryId, name, id, cancellationToken))
            {
                return (DuplicateName(), null);
            }

            items.SetOriginalVersion(item, version);
            item.CategoryId = category.CategoryId;
            item.Category = category;
            item.ItemName = name;
            item.Description = description;
            item.ImageUrl = imageUrl;
            return (new ResponseDTO("Item updated successfully.", 200, true), item);
        }, cancellationToken);
    }

    private async Task<ResponseDTO> WriteAsync(
        Func<Task<(ResponseDTO Response, Item? Item)>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await transaction.ExecuteAsync(action, cancellationToken);
            if (result.Item is not null)
            {
                result.Response.Result = Map(result.Item);
            }

            return result.Response;
        }
        catch (ItemPersistenceException exception)
        {
            logger.LogWarning(exception, "Item persistence failed: {Failure}", exception.Failure);
            return exception.Failure switch
            {
                ItemPersistenceFailure.Duplicate => new ResponseDTO("The item code or name already exists.", 409),
                ItemPersistenceFailure.RelatedDataChanged => new ResponseDTO("Related data has changed. Reload and try again.", 409),
                ItemPersistenceFailure.ConcurrentUpdate => ConcurrentUpdate(),
                ItemPersistenceFailure.Busy => new ResponseDTO("A concurrent operation was detected. Try again.", 409),
                _ => new ResponseDTO("The item data store is unavailable.", 503)
            };
        }
    }

    private static ResponseDTO? ValidateFields(Guid categoryId, string name, string? description, string? imageUrl)
    {
        if (categoryId == Guid.Empty)
        {
            return new ResponseDTO("A valid category ID is required.", 400);
        }

        if (name.Length is < 1 or > 255 || name.Any(char.IsControl))
        {
            return new ResponseDTO("The item name is required, must not exceed 255 characters, and must not contain control characters.", 400);
        }

        if (description?.Length > 2000)
        {
            return new ResponseDTO("The description must not exceed 2000 characters.", 400);
        }

        if (imageUrl is not null && (imageUrl.Length > 2048
            || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return new ResponseDTO("The image URL must be an absolute HTTP or HTTPS URL of at most 2048 characters.", 400);
        }

        return null;
    }

    private static bool TryParseVersion(string? value, out byte[] version)
    {
        version = new byte[8];
        return value is not null && value.Length == 12
            && Convert.TryFromBase64String(value, version, out var bytesWritten)
            && bytesWritten == version.Length;
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ResponseDTO CategoryNotFound()
    {
        return new ResponseDTO("Category not found.", 404);
    }

    private static ResponseDTO ItemNotFound()
    {
        return new ResponseDTO("Item not found.", 404);
    }

    private static ResponseDTO InactiveCategory()
    {
        return new ResponseDTO("The selected category is inactive.", 409);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("The item name already exists in this category.", 409);
    }

    private static ResponseDTO ConcurrentUpdate()
    {
        return new ResponseDTO("The item has been modified. Reload and try again.", 409);
    }

    private static ItemDTO Map(Item item)
    {
        return new ItemDTO
        {
            ItemId = item.ItemId,
            CategoryId = item.CategoryId,
            CategoryName = item.Category.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Description = item.Description,
            ImageUrl = item.ImageUrl,
            Availability = item.Availability,
            Version = Convert.ToBase64String(item.Version)
        };
    }
}
