using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface IKitchenStationRepository
{
    Task<(List<KitchenStation> Items, int Total)> GetPageAsync(int page, int pageSize, string? search, bool? isActive, CancellationToken ct);
    Task<KitchenStation?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(string code, Guid excludedId, CancellationToken ct);
    Task CreateAsync(KitchenStation station, CancellationToken ct);
}
