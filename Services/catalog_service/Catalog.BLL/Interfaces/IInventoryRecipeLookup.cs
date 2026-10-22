using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IInventoryRecipeLookup
{
    Task<(decimal Factor, ResponseDTO? Error)> ResolveAsync(Guid ingredientId, Guid unitId, CancellationToken ct);
}
