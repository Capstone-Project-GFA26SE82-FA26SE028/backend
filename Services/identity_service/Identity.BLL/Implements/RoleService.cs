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
        var actor = await unitOfWork.UserRepo.FindUserAsync(actorId, ct);
        return actor is { IsActive: true }
            && settings.Value.AdminRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
    }

    public async Task<ResponseDTO> GetAll(Guid actorId, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("Không có quyền quản lý role.", 403);
        }

        var roles = await repository.GetAllAsync(ct);
        return new ResponseDTO("Thành công.", 200, true, roles.Select(Map).ToList());
    }

    public async Task<ResponseDTO> GetById(Guid actorId, string id, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return new ResponseDTO("Không có quyền quản lý role.", 403);
        }

        if (!ValidId(id))
        {
            return new ResponseDTO("RoleId không hợp lệ.", 400);
        }

        var role = await repository.GetByIdAsync(id.Trim(), ct);
        return role is null
            ? new ResponseDTO("Không tìm thấy role.", 404)
            : new ResponseDTO("Thành công.", 200, true, Map(role));
    }

    public Task<ResponseDTO> Create(Guid actorId, RoleDTO request, CancellationToken ct)
    {
        return Save(actorId, request.RoleId, request, true, ct);
    }

    public Task<ResponseDTO> Update(Guid actorId, string id, RoleDTO request, CancellationToken ct)
    {
        return Save(actorId, id, request, false, ct);
    }

    private async Task<ResponseDTO> Save(Guid actorId, string id, RoleDTO request, bool create, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await IsAdmin(actorId, ct))
            {
                return new ResponseDTO("Không có quyền quản lý role.", 403);
            }

            if (!ValidId(id) || string.IsNullOrWhiteSpace(request.RoleName)
                || request.RoleName.Trim().Length > 50 || request.Description?.Length > 2048
                || (!create && !string.Equals(id.Trim(), request.RoleId?.Trim(), StringComparison.Ordinal)))
            {
                return new ResponseDTO("RoleId tối đa 50 ký tự, tên bắt buộc tối đa 50 ký tự, mô tả tối đa 2048 ký tự. Không được đổi RoleId.", 400);
            }

            id = id.Trim();
            var role = await repository.GetByIdAsync(id, ct);
            if (create && role is not null)
            {
                return new ResponseDTO("RoleId đã tồn tại.", 409);
            }

            if (!create && role is null)
            {
                return new ResponseDTO("Không tìm thấy role.", 404);
            }

            var oldValue = role is null ? null : Map(role);
            if (role is null)
            {
                role = new Role
                {
                    RoleId = id
                };
                repository.Create(role);
            }

            role.RoleName = request.RoleName.Trim();
            role.Description = string.IsNullOrWhiteSpace(request.Description) ? null! : request.Description.Trim();
            var result = Map(role);
            unitOfWork.RecordAudit(actorId, create ? "CREATE_ROLE" : "UPDATE_ROLE", "Role", role.RoleId,
                oldValue, result);
            return new ResponseDTO("Thành công.", create ? 201 : 200, true, result);
        }, ct);
    }

    public async Task<ResponseDTO> Delete(Guid actorId, string id, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await IsAdmin(actorId, ct))
            {
                return new ResponseDTO("Không có quyền quản lý role.", 403);
            }

            if (!ValidId(id))
            {
                return new ResponseDTO("RoleId không hợp lệ.", 400);
            }

            var role = await repository.GetByIdAsync(id.Trim(), ct);
            if (role is null)
            {
                return new ResponseDTO("Không tìm thấy role.", 404);
            }

            if (settings.Value.AdminRoleIds.Contains(role.RoleId, StringComparer.OrdinalIgnoreCase)
                || await repository.IsUsedAsync(role.RoleId, ct))
            {
                return new ResponseDTO("Không thể xóa role quản trị hoặc role đang được tài khoản sử dụng.", 409);
            }

            unitOfWork.RecordAudit(actorId, "DELETE_ROLE", "Role", role.RoleId, Map(role));
            repository.Delete(role);
            return new ResponseDTO("Đã xóa role.", 200, true);
        }, ct);
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

