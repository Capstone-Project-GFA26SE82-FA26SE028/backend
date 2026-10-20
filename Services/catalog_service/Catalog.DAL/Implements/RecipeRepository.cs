using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.DAL.Implements;

public sealed class RecipeRepository(CatalogDbContext context) : IRecipeRepository
{
    public async Task<RecipeVersion?> GetActiveForItemAsync(
        Guid itemId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        return await context.RecipeVersions
            .AsNoTracking()
            .Include(v => v.RecipeIngredients)
            .Include(v => v.RecipeMedia.OrderBy(m => m.DisplayOrder))
            .Where(v => v.ItemId == itemId
                && v.EffectiveFrom <= at
                && (v.EffectiveTo == null || v.EffectiveTo > at))
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RecipeVersion?> GetActiveForOptionAsync(
        Guid optionId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        return await context.RecipeVersions
            .AsNoTracking()
            .Include(v => v.RecipeIngredients)
            .Include(v => v.RecipeMedia.OrderBy(m => m.DisplayOrder))
            .Where(v => v.OptionId == optionId
                && v.EffectiveFrom <= at
                && (v.EffectiveTo == null || v.EffectiveTo > at))
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RecipeVersion?> GetByIdAsync(
        Guid recipeVersionId,
        CancellationToken cancellationToken)
    {
        return await context.RecipeVersions
            .AsNoTracking()
            .Include(v => v.RecipeIngredients)
            .Include(v => v.RecipeMedia.OrderBy(m => m.DisplayOrder))
            .FirstOrDefaultAsync(v => v.RecipeVersionId == recipeVersionId, cancellationToken);
    }

    public async Task<List<RecipeVersion>> GetHistoryForItemAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        return await context.RecipeVersions
            .AsNoTracking()
            .Include(v => v.RecipeIngredients)
            .Include(v => v.RecipeMedia.OrderBy(m => m.DisplayOrder))
            .Where(v => v.ItemId == itemId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RecipeVersion>> GetHistoryForOptionAsync(
        Guid optionId,
        CancellationToken cancellationToken)
    {
        return await context.RecipeVersions
            .AsNoTracking()
            .Include(v => v.RecipeIngredients)
            .Include(v => v.RecipeMedia.OrderBy(m => m.DisplayOrder))
            .Where(v => v.OptionId == optionId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetNextVersionNumberForItemAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var maxVersion = await context.RecipeVersions
            .Where(v => v.ItemId == itemId)
            .Select(v => (int?)v.Version)
            .MaxAsync(cancellationToken);

        return (maxVersion ?? 0) + 1;
    }

    public async Task<int> GetNextVersionNumberForOptionAsync(
        Guid optionId,
        CancellationToken cancellationToken)
    {
        var maxVersion = await context.RecipeVersions
            .Where(v => v.OptionId == optionId)
            .Select(v => (int?)v.Version)
            .MaxAsync(cancellationToken);

        return (maxVersion ?? 0) + 1;
    }

    public async Task CloseActiveVersionForItemAsync(
        Guid itemId,
        DateTime effectiveTo,
        CancellationToken cancellationToken)
    {
        var activeVersions = await context.RecipeVersions
            .Where(v => v.ItemId == itemId
                && (v.EffectiveTo == null || v.EffectiveTo > effectiveTo)
                && v.EffectiveFrom < effectiveTo)
            .ToListAsync(cancellationToken);

        foreach (var version in activeVersions)
        {
            version.EffectiveTo = effectiveTo;
        }
    }

    public async Task CloseActiveVersionForOptionAsync(
        Guid optionId,
        DateTime effectiveTo,
        CancellationToken cancellationToken)
    {
        var activeVersions = await context.RecipeVersions
            .Where(v => v.OptionId == optionId
                && (v.EffectiveTo == null || v.EffectiveTo > effectiveTo)
                && v.EffectiveFrom < effectiveTo)
            .ToListAsync(cancellationToken);

        foreach (var version in activeVersions)
        {
            version.EffectiveTo = effectiveTo;
        }
    }

    public async Task<RecipeVersion> AddAsync(
        RecipeVersion version,
        CancellationToken cancellationToken)
    {
        await context.RecipeVersions.AddAsync(version, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return version;
    }
}
