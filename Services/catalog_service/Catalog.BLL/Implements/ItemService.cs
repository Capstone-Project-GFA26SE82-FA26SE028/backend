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
    IPriceRepository prices,
    IRecipeRepository recipes,
    IOptions<PricingSettings> pricingSettings,
    TimeProvider timeProvider,
    IKitchenStationLookup kitchenStations,
    ILogger<ItemService> logger) : IItemService
{
    public async Task<ResponseDTO> SetKitchenStation(Guid id, SetItemKitchenStationDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty || request.KitchenStationId == Guid.Empty
            || !TryParseVersion(request.Version, out var version))
        {
            return new ResponseDTO("Yêu cầu mã món ăn, trạm bếp và rowversion hợp lệ.", 400);
        }
        return await WriteAsync(async () =>
        {
            var item = await items.GetByIdAsync(id, cancellationToken);
            if (item is null)
            {
                return (new ResponseDTO("Không tìm thấy món ăn.", 404), (Item?)null);
            }
            if (!item.Version.SequenceEqual(version))
            {
                return (ConcurrentUpdate(), (Item?)null);
            }
            if (request.KitchenStationId.HasValue)
            {
                var error = await kitchenStations.ValidateAsync(request.KitchenStationId.Value, cancellationToken);
                if (error is not null)
                {
                    return (error, (Item?)null);
                }
            }
            items.SetOriginalVersion(item, version);
            item.KitchenStationRef = request.KitchenStationId;
            return (new ResponseDTO("Cập nhật trạm bếp cho món ăn thành công.", 200, true), item);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> GetAll(ItemQueryDTO query, CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);
        if (query is null || !query.IsValid())
        {
            return new ResponseDTO("Phân trang, mã danh mục hoặc bộ lọc món ăn không hợp lệ.", 400);
        }

        if (query.CategoryId.HasValue
            && await categories.GetByIdAsync(query.CategoryId.Value, cancellationToken) is null)
        {
            return CategoryNotFound();
        }

        var page = await items.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Lấy danh sách món ăn thành công.", 200, true,
            new PagedResult<ItemDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn không hợp lệ.", 400);
        }

        var item = await items.GetByIdAsync(id, cancellationToken);
        return item is null ? ItemNotFound() : new ResponseDTO("Lấy thông tin món ăn thành công.", 200, true, Map(item));
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
            return new ResponseDTO("Tên món ăn phải tạo ra slug từ 1 đến 50 ký tự.", 400);
        }

        var initialAvailability = settings.Value.InitialAvailability;
        if (string.IsNullOrWhiteSpace(initialAvailability)
            || !catalogSettings.Value.NonSellingItemAvailabilities.Contains(initialAvailability, StringComparer.Ordinal))
        {
            logger.LogError("Vui lòng cấu hình ItemManagement:InitialAvailability bằng một giá trị trong CatalogManagement:NonSellingItemAvailabilities.");
            return new ResponseDTO("Chưa thể tạo món ăn cho đến khi cấu hình trạng thái ban đầu.", 503);
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
                return (new ResponseDTO("Mã món ăn được tạo tự động đã tồn tại. Vui lòng chọn tên khác.", 409), null);
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
                KitchenStationRef = null,
                PreparationTimeMinutes = request?.PreparationTimeMinutes.HasValue == true && request.PreparationTimeMinutes.Value > 0
                    ? request.PreparationTimeMinutes.Value
                    : null
            };
            items.Create(item);
            return (new ResponseDTO("Tạo món ăn thành công.", 201, true), item);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdateItemDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn không hợp lệ.", 400);
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
            return new ResponseDTO("Version là bắt buộc và phải là chuỗi Base64 rowversion hợp lệ.", 400);
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
            if (request?.PreparationTimeMinutes.HasValue == true)
            {
                item.PreparationTimeMinutes = request.PreparationTimeMinutes.Value > 0 ? request.PreparationTimeMinutes.Value : null;
            }
            return (new ResponseDTO("Cập nhật món ăn thành công.", 200, true), item);
        }, cancellationToken);
    }

    public async Task<ResponseDTO> SetStatus(
        Guid id,
        SetItemStatusDTO request,
        CancellationToken cancellationToken)
    {
        if (request?.Availability == ItemAvailability.OutOfStock)
        {
            await access.RequireMenuStaffAsync(cancellationToken);
        }
        else
        {
            await access.RequireMenuManagerAsync(cancellationToken);
        }

        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn không hợp lệ.", 400);
        }

        var availability = request?.Availability;
        if (availability is not (ItemAvailability.Available or ItemAvailability.OutOfStock or ItemAvailability.Inactive))
        {
            return new ResponseDTO("Trạng thái phải là Available, OutOfStock hoặc Inactive.", 400);
        }

        if (!TryParseVersion(request?.Version, out var version))
        {
            return new ResponseDTO("Version là bắt buộc và phải là chuỗi Base64 rowversion hợp lệ.", 400);
        }

        var nonSelling = catalogSettings.Value.NonSellingItemAvailabilities;
        if (!nonSelling.Contains(ItemAvailability.Inactive, StringComparer.Ordinal)
            || nonSelling.Contains(ItemAvailability.Available, StringComparer.Ordinal)
            || nonSelling.Contains(ItemAvailability.OutOfStock, StringComparer.Ordinal))
        {
            return new ResponseDTO("Cấu hình trạng thái ngừng bán không hợp lệ.", 503);
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

            if (availability == ItemAvailability.Available)
            {
                if (!item.Category.IsActive)
                {
                    return (InactiveCategory(), null);
                }

                var allChannels = pricingSettings.Value.AllChannelsCode;
                if (string.IsNullOrWhiteSpace(allChannels))
                {
                    return (new ResponseDTO("Cần cấu hình Pricing:AllChannelsCode trước khi mở bán món ăn.", 503), null);
                }

                var now = prices.NormalizeTimestamp(timeProvider.GetUtcNow().UtcDateTime);
                var activeRecipes = await recipes.GetActiveForOrderAsync([id], [], now, cancellationToken);
                if (activeRecipes.Count != 1)
                {
                    return (new ResponseDTO("Món ăn chỉ có thể mở bán khi có đúng một công thức đang hoạt động.", 409), null);
                }

                var standardPrices = (await prices.GetForItemAsync(id, cancellationToken))
                    .Where(price => price.Channel == allChannels
                        && !price.DailyStart.HasValue && !price.DailyEnd.HasValue
                        && price.EffectiveFrom <= now
                        && (!price.EffectiveTo.HasValue || now < price.EffectiveTo.Value))
                    .ToArray();
                if (standardPrices.Length != 1 || standardPrices[0].Amount < 0)
                {
                    return (new ResponseDTO("Món ăn chỉ có thể mở bán khi có đúng một giá tiêu chuẩn hợp lệ đang áp dụng.", 409), null);
                }
            }

            if (availability == ItemAvailability.OutOfStock
                && item.Availability is not (ItemAvailability.Available or ItemAvailability.OutOfStock))
            {
                return (new ResponseDTO("Chỉ món ăn đang mở bán mới có thể đánh dấu hết hàng.", 409), null);
            }

            if (item.Availability == availability)
            {
                return (new ResponseDTO("Món ăn đã ở trạng thái yêu cầu.", 200, true), item);
            }

            items.SetOriginalVersion(item, version);
            item.Availability = availability;
            return (new ResponseDTO("Cập nhật trạng thái món ăn thành công.", 200, true), item);
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
            logger.LogWarning(exception, "Lưu món ăn thất bại: {Failure}", exception.Failure);
            return exception.Failure switch
            {
                ItemPersistenceFailure.Duplicate => new ResponseDTO("Mã hoặc tên món ăn đã tồn tại.", 409),
                ItemPersistenceFailure.RelatedDataChanged => new ResponseDTO("Dữ liệu liên quan đã thay đổi. Vui lòng tải lại và thử lại.", 409),
                ItemPersistenceFailure.ConcurrentUpdate => ConcurrentUpdate(),
                ItemPersistenceFailure.Busy => new ResponseDTO("Phát hiện thao tác đồng thời. Vui lòng thử lại.", 409),
                _ => new ResponseDTO("Kho dữ liệu món ăn hiện không khả dụng.", 503)
            };
        }
    }

    private static ResponseDTO? ValidateFields(Guid categoryId, string name, string? description, string? imageUrl)
    {
        if (categoryId == Guid.Empty)
        {
            return new ResponseDTO("Yêu cầu mã danh mục hợp lệ.", 400);
        }

        if (name.Length is < 1 or > 255 || name.Any(char.IsControl))
        {
            return new ResponseDTO("Tên món ăn là bắt buộc, không được vượt quá 255 ký tự và không chứa ký tự điều khiển.", 400);
        }

        if (description?.Length > 2000)
        {
            return new ResponseDTO("Mô tả không được vượt quá 2000 ký tự.", 400);
        }

        if (imageUrl is not null && (imageUrl.Length > 2048
            || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return new ResponseDTO("Đường dẫn ảnh phải là URL HTTP hoặc HTTPS tuyệt đối và không quá 2048 ký tự.", 400);
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
        return new ResponseDTO("Không tìm thấy danh mục.", 404);
    }

    private static ResponseDTO ItemNotFound()
    {
        return new ResponseDTO("Không tìm thấy món ăn.", 404);
    }

    private static ResponseDTO InactiveCategory()
    {
        return new ResponseDTO("Danh mục đã chọn đang ngừng hoạt động.", 409);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("Tên món ăn đã tồn tại trong danh mục này.", 409);
    }

    private static ResponseDTO ConcurrentUpdate()
    {
        return new ResponseDTO("Món ăn đã bị thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.", 409);
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
            KitchenStationId = item.KitchenStationRef,
            PreparationTimeMinutes = item.PreparationTimeMinutes,
            Version = Convert.ToBase64String(item.Version)
        };
    }
}
