using System.Data;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Persistence.Interfaces;

namespace Catalog.BLL.Implements;

public sealed class OrderValidationService(
    IItemRepository items,
    IPriceRepository prices,
    IRecipeRepository recipes,
    IMenuAccess access,
    IUnitOfWork unitOfWork,
    IOptions<PricingSettings> settings,
    TimeProvider clock) : IOrderValidationService
{
    private const decimal MaximumAmount = 9999999999999999.99m;

    public async Task<ResponseDTO> ValidateOrderItems(
        ValidateOrderItemsDTO request, Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken)
    {
        await access.RequireReadAsync(tableSessionId, guestToken, cancellationToken);
        var invalid = ValidateRequest(request);
        if (invalid is not null)
        {
            return new ResponseDTO(invalid, 400);
        }

        if (!PriceRules.TryConfiguration(settings.Value, out var timeZone))
        {
            return new ResponseDTO("Chưa cấu hình định giá danh mục thực đơn.", 503);
        }

        if (!settings.Value.Channels.Contains(request.Channel, StringComparer.Ordinal))
        {
            return new ResponseDTO("Yêu cầu kênh bán hàng hợp lệ đã cấu hình.", 400);
        }

        // Split queries must see a consistent catalog. This transaction does not reserve
        // availability or prices after the response; Ordering must revalidate at submit.
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var at = prices.NormalizeTimestamp(clock.GetUtcNow().UtcDateTime);
            var itemIds = request.Items.Select(line => line.ItemId).Distinct().ToArray();
            var optionIds = request.Items.SelectMany(line => line.Options).Select(option => option.OptionId).Distinct().ToArray();
            var catalogItems = (await items.GetForOrderValidationAsync(itemIds, cancellationToken))
                .ToDictionary(item => item.ItemId);
            var activeRecipes = await recipes.GetActiveForOrderAsync(itemIds, optionIds, at, cancellationToken);
            var itemRecipes = activeRecipes.Where(recipe => recipe.ItemId.HasValue).ToLookup(recipe => recipe.ItemId!.Value);
            var optionRecipes = activeRecipes.Where(recipe => recipe.OptionId.HasValue).ToLookup(recipe => recipe.OptionId!.Value);
            var result = new OrderValidationResultDTO
            {
                Channel = request.Channel,
                EvaluatedAt = new DateTimeOffset(at)
            };

            for (var index = 0; index < request.Items.Count; index++)
            {
                var selection = request.Items[index];
                var error = ValidateLine(index, selection, catalogItems.GetValueOrDefault(selection.ItemId),
                    itemRecipes, optionRecipes, request.Channel, at, timeZone, out var validated);
                if (error is not null)
                {
                    result.Errors.Add(error);
                }
                else
                {
                    result.Items.Add(validated!);
                }
            }

            if (result.Errors.Count > 0)
            {
                result.Items.Clear();
                return new ResponseDTO("Một số món hoặc tùy chọn không còn khả dụng để đặt hàng.", 409, false, result);
            }

            var total = result.Items.Sum(item => item.LineTotal);
            if (total > MaximumAmount)
            {
                return new ResponseDTO("Tổng giá trị đơn hàng vượt quá giới hạn tiền tệ cho phép.", 400);
            }

            result.TotalAmount = total;
            return new ResponseDTO("Xác thực các món đã chọn thành công.", 200, true, result);
        }, IsolationLevel.Serializable, cancellationToken);
    }

    private OrderValidationErrorDTO? ValidateLine(
        int index, OrderSelectionDTO selection, Item? item,
        ILookup<Guid, RecipeVersion> itemRecipes, ILookup<Guid, RecipeVersion> optionRecipes,
        string channel, DateTime at, TimeZoneInfo timeZone, out ValidatedOrderItemDTO? validated)
    {
        validated = null;
        if (item is null || !item.Category.IsActive || item.Availability == ItemAvailability.Inactive)
        {
            return Error(index, selection.ItemId, "ItemUnavailable", "Món ăn không tồn tại hoặc đã ngừng kinh doanh.");
        }

        if (item.Availability != ItemAvailability.Available)
        {
            return Error(index, item.ItemId, "ItemOutOfStock", "Món ăn hiện tạm thời hết hàng.");
        }

        var price = PriceRules.ResolveForSale(item.Prices, channel, settings.Value, at, timeZone);
        if (price is null || !ValidMoney(price.Amount))
        {
            return Error(index, item.ItemId, "PriceUnavailable", "Không có mức giá hợp lệ rõ ràng cho món ăn này.");
        }

        var recipeVersions = itemRecipes[item.ItemId].ToArray();
        if (recipeVersions.Length != 1)
        {
            return Error(index, item.ItemId, "RecipeUnavailable", "Món ăn phải có đúng một công thức đang áp dụng.");
        }

        var allowed = item.ItemOptionGroups.SelectMany(link => link.OptionGroup.Options)
            .ToDictionary(option => option.OptionId);
        var optionSnapshots = new List<ValidatedOrderOptionDTO>();
        foreach (var chosen in selection.Options)
        {
            if (!allowed.TryGetValue(chosen.OptionId, out var option))
            {
                return Error(index, item.ItemId, "OptionNotAllowed", "Tùy chọn không thuộc món ăn này.", chosen.OptionId);
            }

            if (option.Availability != ItemAvailability.Available || !ValidMoney(option.ExtraPrice))
            {
                return Error(index, item.ItemId, "OptionUnavailable", "Tùy chọn không khả dụng hoặc giá không hợp lệ.", chosen.OptionId);
            }

            var versions = optionRecipes[option.OptionId].ToArray();
            if (versions.Length > 1)
            {
                return Error(index, item.ItemId, "OptionRecipeConflict", "Tùy chọn có công thức đang hoạt động bị trùng lặp.", chosen.OptionId);
            }

            optionSnapshots.Add(new ValidatedOrderOptionDTO
            {
                OptionId = option.OptionId,
                NameSnapshot = option.OptionName,
                ExtraPriceSnapshot = option.ExtraPrice,
                Quantity = chosen.Quantity,
                RecipeVersionId = versions.SingleOrDefault()?.RecipeVersionId
            });
        }

        foreach (var link in item.ItemOptionGroups)
        {
            var quantity = selection.Options.Where(option => allowed[option.OptionId].OptionGroupId == link.OptionGroupId)
                .Sum(option => option.Quantity);
            if (link.MinSelection < 0 || link.MaxSelection < 1 || link.MinSelection > link.MaxSelection
                || quantity < link.MinSelection || quantity > link.MaxSelection)
            {
                return Error(index, item.ItemId, "SelectionLimit", "Số lượng tùy chọn phải thỏa mãn giới hạn tối thiểu/tối đa của nhóm.",
                    optionGroupId: link.OptionGroupId);
            }
        }

        var optionsTotal = optionSnapshots.Sum(option => option.ExtraPriceSnapshot * option.Quantity);
        var unitTotal = price.Amount + optionsTotal;
        var lineTotal = unitTotal * selection.Quantity;
        if (unitTotal > MaximumAmount || lineTotal > MaximumAmount)
        {
            return Error(index, item.ItemId, "AmountOutOfRange", "Tổng dòng vượt quá giới hạn tiền tệ cho phép.");
        }

        validated = new ValidatedOrderItemDTO
        {
            LineIndex = index,
            ItemId = item.ItemId,
            NameSnapshot = item.ItemName,
            PriceId = price.PriceId,
            RecipeVersionId = recipeVersions[0].RecipeVersionId,
            KitchenStationId = item.KitchenStationRef,
            PreparationTimeMinutes = item.PreparationTimeMinutes,
            Quantity = selection.Quantity,
            UnitPriceSnapshot = price.Amount,
            OptionsTotalPerUnit = optionsTotal,
            UnitTotal = unitTotal,
            LineTotal = lineTotal,
            Options = optionSnapshots
        };
        return null;
    }

    private static string? ValidateRequest(ValidateOrderItemsDTO? request)
    {
        if (request?.Items is null || request.Items.Count is < 1 or > 100)
        {
            return "Vui lòng chọn từ 1 đến 100 dòng món đặt.";
        }

        foreach (var line in request.Items)
        {
            if (line is null || line.ItemId == Guid.Empty || line.Quantity is < 1 or > 1000
                || line.Options is null || line.Options.Count > 100)
            {
                return "Mỗi dòng yêu cầu mã món, số lượng từ 1-1000 và tối đa 100 tùy chọn.";
            }

            var ids = new HashSet<Guid>();
            foreach (var option in line.Options)
            {
                if (option is null || option.OptionId == Guid.Empty || option.Quantity is < 1 or > 1000
                    || !ids.Add(option.OptionId))
                {
                    return "Mã tùy chọn phải hợp lệ và duy nhất trong một dòng, số lượng từ 1-1000 cho mỗi phần món.";
                }
            }
        }

        return null;
    }

    private static bool ValidMoney(decimal amount)
    {
        return amount >= 0 && amount <= MaximumAmount && decimal.Round(amount, 2) == amount;
    }

    private static OrderValidationErrorDTO Error(
        int index, Guid itemId, string code, string message, Guid? optionId = null, Guid? optionGroupId = null)
    {
        return new OrderValidationErrorDTO
        {
            LineIndex = index,
            ItemId = itemId,
            OptionId = optionId,
            OptionGroupId = optionGroupId,
            Code = code,
            Message = message
        };
    }
}
