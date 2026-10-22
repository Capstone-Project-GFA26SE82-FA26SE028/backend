using Catalog.BLL.Interfaces;
using Shared.Common.DTOs;

namespace Catalog.Tests;

internal sealed class TestInventoryLookup : IInventoryRecipeLookup
{
    public Dictionary<(Guid, Guid), decimal> Factors { get; } = [];
    public int? FailureStatus { get; set; }

    public Task<(decimal Factor, ResponseDTO? Error)> ResolveAsync(Guid ingredientId, Guid unitId, CancellationToken ct)
    {
        ResponseDTO? error = FailureStatus.HasValue ? new ResponseDTO("Inventory rejected the ingredient.", FailureStatus.Value) : null;
        return Task.FromResult((Factors.GetValueOrDefault((ingredientId, unitId), 1m), error));
    }
}
