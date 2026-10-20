using Catalog.DAL.Models;

namespace Catalog.DAL.Interfaces;

public interface IRecipeRepository
{
    DateTime NormalizeTimestamp(DateTime utc);

    Task<DateTime?> GetLatestEffectiveFromAsync(Guid? itemId, Guid? optionId, CancellationToken cancellationToken);

    Task<List<RecipeVersion>> GetActiveForOrderAsync(Guid[] itemIds, Guid[] optionIds, DateTime at,
        CancellationToken cancellationToken);

    Task<RecipeVersion?> GetActiveForItemAsync(
        Guid itemId,
        DateTime at,
        CancellationToken cancellationToken);

    Task<RecipeVersion?> GetActiveForOptionAsync(
        Guid optionId,
        DateTime at,
        CancellationToken cancellationToken);

    Task<RecipeVersion?> GetByIdAsync(
        Guid recipeVersionId,
        CancellationToken cancellationToken);

    Task<List<RecipeVersion>> GetHistoryForItemAsync(
        Guid itemId,
        CancellationToken cancellationToken);

    Task<List<RecipeVersion>> GetHistoryForOptionAsync(
        Guid optionId,
        CancellationToken cancellationToken);

    Task<int> GetNextVersionNumberForItemAsync(
        Guid itemId,
        CancellationToken cancellationToken);

    Task<int> GetNextVersionNumberForOptionAsync(
        Guid optionId,
        CancellationToken cancellationToken);

    Task CloseActiveVersionForItemAsync(
        Guid itemId,
        DateTime effectiveTo,
        CancellationToken cancellationToken);

    Task CloseActiveVersionForOptionAsync(
        Guid optionId,
        DateTime effectiveTo,
        CancellationToken cancellationToken);

    Task<RecipeVersion> AddAsync(
        RecipeVersion version,
        CancellationToken cancellationToken);
}
