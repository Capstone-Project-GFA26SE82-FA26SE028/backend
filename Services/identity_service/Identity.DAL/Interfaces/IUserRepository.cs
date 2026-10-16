using Shared.Persistence.Interfaces;
using Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Identity.DAL.Interfaces
{
    public interface IUserRepository : IGenericRepository<User>
    {
        Task<bool> LoginExistsAsync(Guid exceptId, string name, string email, CancellationToken ct);
        Task<bool> ChangePasswordAsync(Guid userId, string oldHash, string newHash, CancellationToken ct);
        Task<User?> FindByEmailAsync(string email);
        Task<User?> FindUserAsync(Guid id, CancellationToken ct);
        Task<(List<User> Items, int Total)> GetAllAsync(int page, int pageSize, string? search, CancellationToken ct, Identity.Common.DTOs.UserFilter? filter = null);
        Task<(int Status, User? User)> CreateEmployeeAsync(Guid actorId, User user, string[] adminRoles, CancellationToken ct);
        Task<(int Status, User? User)> ChangeAccessAsync(Guid actorId, Guid id, string? roleId, bool? isActive, string[] adminRoles, CancellationToken ct);

    }
}
