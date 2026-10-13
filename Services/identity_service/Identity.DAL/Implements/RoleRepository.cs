using Identity.DAL.Interfaces;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.DAL.Implements;

public sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    public Task<List<Role>> GetAllAsync(CancellationToken ct)
    {
        return context.Roles.AsNoTracking().OrderBy(role => role.RoleId).ToListAsync(ct);
    }

    public Task<Role?> GetByIdAsync(string id, CancellationToken ct)
    {
        return context.Roles.FirstOrDefaultAsync(role => role.RoleId == id, ct);
    }

    public Task<bool> IsUsedAsync(string id, CancellationToken ct)
    {
        return context.Users.AnyAsync(user => user.RoleId == id, ct);
    }

    public void Create(Role role)
    {
        context.Roles.Add(role);
    }

    public void Delete(Role role)
    {
        context.Roles.Remove(role);
    }
}

