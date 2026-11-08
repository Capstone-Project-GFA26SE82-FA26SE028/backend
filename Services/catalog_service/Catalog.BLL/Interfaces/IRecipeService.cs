using Catalog.Common.DTOs;
using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IRecipeService
{
    Task<ResponseDTO> CreateItemRecipe(
        Guid itemId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetActiveItemRecipe(
        Guid itemId,
        DateTime? at,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetItemRecipeHistory(
        Guid itemId,
        CancellationToken cancellationToken);

    Task<ResponseDTO> CreateOptionRecipe(
        Guid optionId,
        CreateRecipeVersionDTO request,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetActiveOptionRecipe(
        Guid optionId,
        DateTime? at,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetOptionRecipeHistory(
        Guid optionId,
        CancellationToken cancellationToken);

    Task<ResponseDTO> GetById(
        Guid recipeVersionId,
        CancellationToken cancellationToken);
}
