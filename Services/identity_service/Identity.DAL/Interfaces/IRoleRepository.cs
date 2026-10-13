using Identity.Domain.Models;

namespace Identity.DAL.Interfaces;

public interface IRoleRepository
{
    Task<List<Role>> GetAllAsync(CancellationToken ct);
    Task<Role?> GetByIdAsync(string id, CancellationToken ct);
    Task<bool> IsUsedAsync(string id, CancellationToken ct);
    void Create(Role role);
    void Delete(Role role);
}

