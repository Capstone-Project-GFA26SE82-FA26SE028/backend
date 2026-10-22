using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.DAL.Interfaces;

public interface ISessionTableRepository : IGenericRepository<SessionTable>
{
    Task<IReadOnlyList<SessionTable>> GetUnreleasedForTablesAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken ct);
}
