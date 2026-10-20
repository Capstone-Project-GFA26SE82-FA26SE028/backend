using System.Data;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Shared.Common.DTOs;
using Shared.Persistence.Interfaces;

namespace Catalog.BLL.Implements;

public sealed class RecipeService(
    IRecipeRepository recipes,
    IItemRepository items,
    IOptionRepository options,
    ICatalogAccess access,
    IUnitOfWork unitOfWork) : IRecipeService
{
    public async Task<ResponseDTO> CreateItemRecipe(
        Guid itemId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Item ID is required.", 400);
        }

        var validationError = ValidateRecipe(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Item not found.", 404);
        }

        var effectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow;

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
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

            return new ResponseDTO("Recipe created successfully.", 201, true, Map(entity));
        }, IsolationLevel.Serializable, cancellationToken);
    }

    public async Task<ResponseDTO> GetActiveItemRecipe(
        Guid itemId,
        DateTime? at,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Item ID is required.", 400);
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Item not found.", 404);
        }

        var queryTime = at ?? DateTime.UtcNow;
        var active = await recipes.GetActiveForItemAsync(itemId, queryTime, cancellationToken);
        if (active is null)
        {
            return new ResponseDTO("No active recipe found for this item.", 404);
        }

        return new ResponseDTO("Active recipe retrieved successfully.", 200, true, Map(active));
    }

    public async Task<ResponseDTO> GetItemRecipeHistory(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (itemId == Guid.Empty)
        {
            return new ResponseDTO("Item ID is required.", 400);
        }

        var item = await items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ResponseDTO("Item not found.", 404);
        }

        var history = await recipes.GetHistoryForItemAsync(itemId, cancellationToken);
        var result = history.Select(Map).ToList();

        return new ResponseDTO("Recipe history retrieved successfully.", 200, true, result);
    }

    public async Task<ResponseDTO> CreateOptionRecipe(
        Guid optionId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Option ID is required.", 400);
        }

        var validationError = ValidateRecipe(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Option not found.", 404);
        }

        var effectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow;

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
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

            return new ResponseDTO("Recipe created successfully.", 201, true, Map(entity));
        }, IsolationLevel.Serializable, cancellationToken);
    }

    public async Task<ResponseDTO> GetActiveOptionRecipe(
        Guid optionId,
        DateTime? at,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Option ID is required.", 400);
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Option not found.", 404);
        }

        var queryTime = at ?? DateTime.UtcNow;
        var active = await recipes.GetActiveForOptionAsync(optionId, queryTime, cancellationToken);
        if (active is null)
        {
            return new ResponseDTO("No active recipe found for this option.", 404);
        }

        return new ResponseDTO("Active recipe retrieved successfully.", 200, true, Map(active));
    }

    public async Task<ResponseDTO> GetOptionRecipeHistory(
        Guid optionId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);

        if (optionId == Guid.Empty)
        {
            return new ResponseDTO("Option ID is required.", 400);
        }

        var option = await options.GetByIdAsync(optionId, cancellationToken);
        if (option is null)
        {
            return new ResponseDTO("Option not found.", 404);
        }

        var history = await recipes.GetHistoryForOptionAsync(optionId, cancellationToken);
        var result = history.Select(Map).ToList();

        return new ResponseDTO("Recipe history retrieved successfully.", 200, true, result);
    }

    public async Task<ResponseDTO> GetById(
        Guid recipeVersionId,
        CancellationToken cancellationToken)
    {
        await access.RequireMenuStaffAsync(cancellationToken);

        if (recipeVersionId == Guid.Empty)
        {
            return new ResponseDTO("Recipe version ID is required.", 400);
        }

        var entity = await recipes.GetByIdAsync(recipeVersionId, cancellationToken);
        if (entity is null)
        {
            return new ResponseDTO("Recipe not found.", 404);
        }

        return new ResponseDTO("Recipe retrieved successfully.", 200, true, Map(entity));
    }

    private static ResponseDTO? ValidateRecipe(CreateRecipeVersionDTO request)
    {
        if (request is null)
        {
            return new ResponseDTO("Recipe request cannot be empty.", 400);
        }

        if (request.Ingredients is null || request.Ingredients.Count == 0)
        {
            return new ResponseDTO("Recipe must have at least one ingredient.", 400);
        }

        var ingredientRefs = new HashSet<Guid>();
        foreach (var ingredient in request.Ingredients)
        {
            if (ingredient.IngredientRef == Guid.Empty || ingredient.UnitRef == Guid.Empty)
            {
                return new ResponseDTO("Invalid ingredient reference or unit reference.", 400);
            }

            if (ingredient.Quantity <= 0)
            {
                return new ResponseDTO("Ingredient quantity must be greater than 0.", 400);
            }

            if (ingredient.ConversionFactor.HasValue && ingredient.ConversionFactor.Value <= 0)
            {
                return new ResponseDTO("Conversion factor must be greater than 0.", 400);
            }

            if (!ingredientRefs.Add(ingredient.IngredientRef))
            {
                return new ResponseDTO("Duplicate ingredient in recipe.", 400);
            }
        }

        if (request.Media is not null)
        {
            foreach (var media in request.Media)
            {
                if (string.IsNullOrWhiteSpace(media.MediaType) || media.MediaType.Length > 50)
                {
                    return new ResponseDTO("Media type must not be empty and at most 50 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(media.Url) || media.Url.Length > 2048)
                {
                    return new ResponseDTO("Media URL must not be empty and at most 2048 characters.", 400);
                }

                if (media.DisplayOrder < 0)
                {
                    return new ResponseDTO("Media display order must be non-negative.", 400);
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
