using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace Identity.BLL.Implements;

public sealed class RoleService(IRoleRepository repository, IUnitOfWork unitOfWork,
    IOptions<UserManagementSettings> settings) : IRoleService
{
    private async Task<bool> IsAdmin(Guid actorId, CancellationToken ct)
    {
        var adminRoles = settings.Value.AdminRoleIds;
        return await unitOfWork.UserRepo.AnyAsync(actor => actor.UserId == actorId
            && actor.IsActive && adminRoles.Contains(actor.RoleId));
    }

    public async Task<ResponseDTO> GetAll(Guid actorId, CancellationToken ct, string? search = null)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("You do not have permission to manage roles.", 403);
        }

        if (search?.Length > 50)
        {
            return new ResponseDTO("Role search must not exceed 50 characters.", 400);
        }

        var roles = await repository.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            roles = roles.Where(role => role.RoleId.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return new ResponseDTO("Success.", 200, true, roles.Select(Map).ToList());
    }

    public async Task<ResponseDTO> GetById(Guid actorId, string id, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("You do not have permission to manage roles.", 403);
        }

        if (!ValidId(id))
        {
            return new ResponseDTO("Invalid RoleId.", 400);
        }

        var role = await repository.GetByIdAsync(id.Trim(), ct);
        return role is null
            ? new ResponseDTO("Role not found.", 404)
            : new ResponseDTO("Success.", 200, true, Map(role));
    }

    public Task<ResponseDTO> Create(Guid actorId, RoleDTO request, CancellationToken ct)
    {
        return DenyMutation(actorId, ct);
    }

    public Task<ResponseDTO> Update(Guid actorId, string id, RoleDTO request, CancellationToken ct)
    {
        return DenyMutation(actorId, ct);
    }

    public async Task<ResponseDTO> Delete(Guid actorId, string id, CancellationToken ct)
    {
        return await DenyMutation(actorId, ct);
    }

    private async Task<ResponseDTO> DenyMutation(Guid actorId, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("You do not have permission to manage roles.", 403);
        }

        return new ResponseDTO("System roles are fixed. Roles can only be viewed and assigned to accounts.", 409);
    }
    private static bool ValidId(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && id.Trim().Length <= 50
            && id.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');
    }

    private static RoleDTO Map(Role role)
    {
        return new RoleDTO(role.RoleId, role.RoleName, role.Description);
    }
}

