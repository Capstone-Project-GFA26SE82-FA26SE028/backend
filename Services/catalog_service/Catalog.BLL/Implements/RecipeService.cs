using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Shared.Common.DTOs;
using Catalog.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Catalog.BLL.Implements;

public sealed class RecipeService(
    IRecipeRepository recipes,
    IItemRepository items,
    IOptionRepository options,
    ICatalogAccess access,
    IRecipeTransaction transaction,
    ILogger<RecipeService> logger,
    IInventoryRecipeLookup inventory) : IRecipeService
{
    public async Task<ResponseDTO> CreateItemRecipe(
        Guid itemId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn là bắt buộc.", 400);
        }

        var validationError = ValidateRecipe(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Không tìm thấy món ăn.", 404);
        }

        var inventoryError = await ResolveIngredients(request, cancellationToken);
        if (inventoryError is not null)
        {
            return inventoryError;
        }

        var effectiveFrom = recipes.NormalizeTimestamp(request.EffectiveFrom ?? DateTime.UtcNow);

        return await WriteAsync(async () =>
        {
            var latest = await recipes.GetLatestEffectiveFromAsync(itemId, null, cancellationToken);
            if (latest.HasValue && effectiveFrom <= latest.Value)
            {
                return new ResponseDTO("Công thức mới phải bắt đầu sau phiên bản gần nhất đã lên lịch. Không được trùng hoặc trước thời gian đó.", 409);
            }

            await recipes.CloseActiveVersionForItemAsync(itemId, effectiveFrom, cancellationToken);

            var nextVersion = await recipes.GetNextVersionNumberForItemAsync(itemId, cancellationToken);

            var versionId = Guid.NewGuid();
            var entity = new RecipeVersion
            {
                RecipeVersionId = versionId,
                ItemId = itemId,
                Version = nextVersion,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                Instructions = request.Instructions?.Trim() ?? string.Empty
            };

            PopulateIngredientsAndMedia(entity, versionId, request);

            await recipes.AddAsync(entity, cancellationToken);

            return new ResponseDTO("Tạo công thức thành công.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> GetActiveItemRecipe(
        Guid itemId,
        DateTime? at,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn là bắt buộc.", 400);
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Không tìm thấy món ăn.", 404);
        }

        if (at.HasValue)
        {
            await access.RequireMenuManagerAsync(cancellationToken);
        }

        var queryTime = at ?? DateTime.UtcNow;
        var active = await recipes.GetActiveForItemAsync(itemId, queryTime, cancellationToken);
        if (active is null)
        {
            return new ResponseDTO("Không tìm thấy công thức đang hoạt động cho món ăn này.", 404);
        }

        return new ResponseDTO("Lấy công thức đang hoạt động thành công.", 200, true, Map(active));
    }

    public async Task<ResponseDTO> GetItemRecipeHistory(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Mã món ăn là bắt buộc.", 400);
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Không tìm thấy món ăn.", 404);
        }

        var history = await recipes.GetHistoryForItemAsync(itemId, cancellationToken);
        var result = history.Select(Map).ToList();

        return new ResponseDTO("Lấy lịch sử công thức thành công.", 200, true, result);
    }

    public async Task<ResponseDTO> CreateOptionRecipe(
        Guid optionId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Mã tùy chọn là bắt buộc.", 400);
        }

        var validationError = ValidateRecipe(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Không tìm thấy tùy chọn.", 404);
        }

        var inventoryError = await ResolveIngredients(request, cancellationToken);
        if (inventoryError is not null)
        {
            return inventoryError;
        }

        var effectiveFrom = recipes.NormalizeTimestamp(request.EffectiveFrom ?? DateTime.UtcNow);

        return await WriteAsync(async () =>
        {
            var latest = await recipes.GetLatestEffectiveFromAsync(null, optionId, cancellationToken);
            if (latest.HasValue && effectiveFrom <= latest.Value)
            {
                return new ResponseDTO("Công thức mới phải bắt đầu sau phiên bản gần nhất đã lên lịch. Không được trùng hoặc trước thời gian đó.", 409);
            }

            await recipes.CloseActiveVersionForOptionAsync(optionId, effectiveFrom, cancellationToken);

            var nextVersion = await recipes.GetNextVersionNumberForOptionAsync(optionId, cancellationToken);

            var versionId = Guid.NewGuid();
            var entity = new RecipeVersion
            {
                RecipeVersionId = versionId,
                OptionId = optionId,
                Version = nextVersion,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                Instructions = request.Instructions?.Trim() ?? string.Empty
            };

            PopulateIngredientsAndMedia(entity, versionId, request);

            await recipes.AddAsync(entity, cancellationToken);

            return new ResponseDTO("Tạo công thức thành công.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> GetActiveOptionRecipe(
        Guid optionId,
        DateTime? at,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Mã tùy chọn là bắt buộc.", 400);
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Không tìm thấy tùy chọn.", 404);
        }

        if (at.HasValue)
        {
            await access.RequireMenuManagerAsync(cancellationToken);
        }

        var queryTime = at ?? DateTime.UtcNow;
        var active = await recipes.GetActiveForOptionAsync(optionId, queryTime, cancellationToken);
        if (active is null)
        {
            return new ResponseDTO("Không tìm thấy công thức đang hoạt động cho tùy chọn này.", 404);
        }

        return new ResponseDTO("Lấy công thức đang hoạt động thành công.", 200, true, Map(active));
    }

    public async Task<ResponseDTO> GetOptionRecipeHistory(
        Guid optionId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Mã tùy chọn là bắt buộc.", 400);
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Không tìm thấy tùy chọn.", 404);
        }

        var history = await recipes.GetHistoryForOptionAsync(optionId, cancellationToken);
        var result = history.Select(Map).ToList();

        return new ResponseDTO("Lấy lịch sử công thức thành công.", 200, true, result);
    }

    public async Task<ResponseDTO> GetById(
        Guid recipeVersionId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (recipeVersionId == Guid.Empty)
        {
            return new ResponseDTO("Mã phiên bản công thức là bắt buộc.", 400);
        }

        var entity = await recipes.GetByIdAsync(recipeVersionId, cancellationToken);
        if (entity is null)
        {
            return new ResponseDTO("Không tìm thấy công thức.", 404);
        }

        var now = DateTime.UtcNow;
        if (entity.EffectiveFrom > now || (entity.EffectiveTo.HasValue && now >= entity.EffectiveTo.Value))
        {
            await access.RequireMenuManagerAsync(cancellationToken);
        }

        return new ResponseDTO("Lấy thông tin công thức thành công.", 200, true, Map(entity));
    }

    private async Task<ResponseDTO> WriteAsync(Func<Task<ResponseDTO>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteAsync(action, cancellationToken);
        }
        catch (RecipePersistenceException exception)
        {
            logger.LogWarning(exception, "Không thể lưu phiên bản công thức.");
            return exception.IsConflict
                ? new ResponseDTO("Dữ liệu công thức đã bị thay đổi đồng thời. Vui lòng tải lại và thử lại.", 409)
                : new ResponseDTO("Kho dữ liệu công thức hiện không khả dụng.", 503);
        }
    }

    private async Task<ResponseDTO?> ResolveIngredients(CreateRecipeVersionDTO request, CancellationToken ct)
    {
        foreach (var ingredient in request.Ingredients)
        {
            var (factor, error) = await inventory.ResolveAsync(ingredient.IngredientRef, ingredient.UnitRef, ct);
            if (error is not null)
            {
                return error;
            }
            decimal quantity;
            try
            {
                quantity = checked(ingredient.Quantity * factor);
            }
            catch (OverflowException)
            {
                return new ResponseDTO("Định lượng công thức sau quy đổi vượt quá phạm vi cho phép.", 400);
            }
            if (factor <= 0 || factor > 999999999999.999999m || decimal.Round(factor, 6) != factor
                || quantity <= 0 || quantity > 999999999999.999999m || decimal.Round(quantity, 6) != quantity
                || ingredient.Quantity > 999999999999.999999m || decimal.Round(ingredient.Quantity, 6) != ingredient.Quantity)
            {
                return new ResponseDTO("Định lượng công thức phải phù hợp với decimal(18,6) không làm tròn.", 400);
            }
            ingredient.ConversionFactor = factor;
        }
        return null;
    }

    private static ResponseDTO? ValidateRecipe(CreateRecipeVersionDTO request)
    {
        if (request is null)
        {
            return new ResponseDTO("Yêu cầu công thức không được để trống.", 400);
        }

        if (request.EffectiveFrom.HasValue
            && (request.EffectiveFrom.Value.Kind != DateTimeKind.Utc
                || request.EffectiveFrom.Value < new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                || request.EffectiveFrom.Value > new DateTime(9999, 12, 31, 23, 59, 59, 997, DateTimeKind.Utc)))
        {
            return new ResponseDTO("EffectiveFrom phải là mốc thời gian UTC trong phạm vi datetime SQL.", 400);
        }

        if (request.Ingredients is null || request.Ingredients.Count is < 1 or > 100)
        {
            return new ResponseDTO("Công thức phải có ít nhất một nguyên liệu.", 400);
        }

        var ingredientRefs = new HashSet<Guid>();
        foreach (var ingredient in request.Ingredients)
        {
            if (ingredient is null || ingredient.IngredientRef == Guid.Empty || ingredient.UnitRef == Guid.Empty)
            {
                return new ResponseDTO("Mã nguyên liệu hoặc đơn vị không hợp lệ.", 400);
            }

            if (ingredient.Quantity <= 0)
            {
                return new ResponseDTO("Định lượng nguyên liệu phải lớn hơn 0.", 400);
            }

            if (ingredient.ConversionFactor.HasValue && ingredient.ConversionFactor.Value <= 0)
            {
                return new ResponseDTO("Hệ số quy đổi phải lớn hơn 0.", 400);
            }

            if (!ingredientRefs.Add(ingredient.IngredientRef))
            {
                return new ResponseDTO("Nguyên liệu bị trùng lặp trong công thức.", 400);
            }
        }

        if (request.Media is not null)
        {
            foreach (var media in request.Media)
            {
                if (string.IsNullOrWhiteSpace(media.MediaType) || media.MediaType.Length > 50)
                {
                    return new ResponseDTO("Loại tệp đa phương tiện không được rỗng và tối đa 50 ký tự.", 400);
                }

                if (string.IsNullOrWhiteSpace(media.Url) || media.Url.Length > 2048)
                {
                    return new ResponseDTO("Đường dẫn đa phương tiện không được rỗng và tối đa 2048 ký tự.", 400);
                }

                if (media.DisplayOrder < 0)
                {
                    return new ResponseDTO("Thứ tự hiển thị phải là số không âm.", 400);
                }
            }
        }

        return null;
    }

    private static void PopulateIngredientsAndMedia(
        RecipeVersion entity,
        Guid versionId,
        CreateRecipeVersionDTO request)
    {
        foreach (var ing in request.Ingredients)
        {
            var factor = ing.ConversionFactor ?? 1.0m;
            entity.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeIngredientId = Guid.NewGuid(),
                RecipeVersionId = versionId,
                IngredientRef = ing.IngredientRef,
                UnitRef = ing.UnitRef,
                Quantity = ing.Quantity,
                ConversionFactorSnapshot = factor,
                BaseQuantitySnapshot = ing.Quantity * factor
            });
        }

        if (request.Media is not null)
        {
            foreach (var m in request.Media)
            {
                entity.RecipeMedia.Add(new RecipeMedium
                {
                    RecipeMediaId = Guid.NewGuid(),
                    RecipeVersionId = versionId,
                    MediaType = m.MediaType?.Trim() ?? "image",
                    Url = m.Url?.Trim() ?? string.Empty,
                    DisplayOrder = m.DisplayOrder
                });
            }
        }
    }

    private static RecipeVersionDTO Map(RecipeVersion entity)
    {
        return new RecipeVersionDTO
        {
            RecipeVersionId = entity.RecipeVersionId,
            ItemId = entity.ItemId,
            OptionId = entity.OptionId,
            Version = entity.Version,
            EffectiveFrom = entity.EffectiveFrom,
            EffectiveTo = entity.EffectiveTo,
            Instructions = entity.Instructions,
            Ingredients = entity.RecipeIngredients.Select(i => new RecipeIngredientDTO
            {
                RecipeIngredientId = i.RecipeIngredientId,
                RecipeVersionId = i.RecipeVersionId,
                IngredientRef = i.IngredientRef,
                UnitRef = i.UnitRef,
                Quantity = i.Quantity,
                BaseQuantitySnapshot = i.BaseQuantitySnapshot,
                ConversionFactorSnapshot = i.ConversionFactorSnapshot
            }).ToList(),
            Media = entity.RecipeMedia.OrderBy(m => m.DisplayOrder).Select(m => new RecipeMediaDTO
            {
                RecipeMediaId = m.RecipeMediaId,
                RecipeVersionId = m.RecipeVersionId,
                MediaType = m.MediaType,
                Url = m.Url,
                DisplayOrder = m.DisplayOrder
            }).ToList()
        };
    }
}
