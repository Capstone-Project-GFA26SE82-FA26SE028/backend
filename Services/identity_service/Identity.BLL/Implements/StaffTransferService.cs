using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace Identity.BLL.Implements;

public sealed class StaffTransferService(IStaffTransferRepository transfers, IUserRepository users,
    ISessionRepository sessions, IUnitOfWork work, IStaffTransferRemote remote, IRestaurantNames names,
    IOptions<UserManagementSettings> settings) : IStaffTransferService
{
    private static readonly string[] Roles = ["manager", "waiter", "cashier", "kitchen", "inventory"];

    private async Task<bool> IsPlatform(Guid actorId, CancellationToken ct)
    {
        var actor = await users.FindUserAsync(actorId, ct);
        return actor is { IsActive: true } && settings.Value.AdminRoleIds.Contains(actor.RoleId);
    }

    public async Task<ResponseDTO> Create(Guid actorId, Guid id, Guid userId, Guid targetRestaurantId, CancellationToken ct)
    {
        if (!await IsPlatform(actorId, ct))
        {
            return new("Chỉ quản trị nền tảng được chuyển nhân viên giữa các nhà hàng.", 403);
        }

        if (id == Guid.Empty || userId == Guid.Empty || targetRestaurantId == Guid.Empty)
        {
            return new("Mã yêu cầu, nhân viên và nhà hàng phải hợp lệ.", 400);
        }

        if (!remote.IsConfigured)
        {
            return new("Chưa cấu hình dịch vụ chuyển nhân viên.", 503);
        }

        if (!(await names.GetAsync([targetRestaurantId], ct)).ContainsKey(targetRestaurantId))
        {
            return new("Không tìm thấy nhà hàng đích.", 404);
        }

        return await work.ExecuteInTransactionAsync(async () =>
        {
            var user = await users.LockAsync(userId, ct);
            if (!await IsPlatform(actorId, ct))
            {
                return new ResponseDTO("Không có quyền chuyển nhân viên.", 403);
            }

            var existing = await transfers.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                return existing.UserId == userId && existing.TargetRestaurantId == targetRestaurantId
                    ? Response(existing) : new ResponseDTO("Idempotency-Key đã được dùng cho yêu cầu khác.", 409);
            }

            if (user is null)
            {
                return new ResponseDTO("Không tìm thấy nhân viên.", 404);
            }

            if (!Roles.Contains(user.RoleId) || !user.RestaurantId.HasValue || user.ProvisioningPending
                || user.RestaurantId == targetRestaurantId || await transfers.HasPendingAsync(userId, ct))
            {
                return new ResponseDTO("Chỉ chuyển nhân viên đã có nhà hàng, không đang phân công/chuyển; nhà hàng đích phải khác nhà hàng hiện tại.", 409);
            }

            var transfer = new StaffTransfer
            {
                StaffTransferId = id,
                UserId = userId,
                SourceRestaurantId = user.RestaurantId.Value,
                TargetRestaurantId = targetRestaurantId,
                RequestedBy = actorId,
                WasActive = user.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            transfers.Create(transfer);
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await sessions.RevokeForTransferAsync(userId, actorId, ct);
            work.RecordAudit(actorId, "STAFF_TRANSFER_REQUESTED", "User", userId.ToString(),
                new
                {
                    RestaurantId = transfer.SourceRestaurantId
                }, new
                {
                    RestaurantId = targetRestaurantId,
                    TransferId = id
                });
            return Response(transfer);
        }, ct);
    }

    public async Task<ResponseDTO> GetById(Guid actorId, Guid id, CancellationToken ct)
    {
        if (!await IsPlatform(actorId, ct))
        {
            return new("Chỉ quản trị nền tảng được xem yêu cầu chuyển nhân viên.", 403);
        }

        var transfer = await transfers.GetByIdAsync(id, ct);
        return transfer is null ? new("Không tìm thấy yêu cầu chuyển nhân viên.", 404) : Response(transfer);
    }

    public async Task<IReadOnlyList<Guid>> GetPending(CancellationToken ct)
    {
        return await transfers.GetPendingAsync(ct);
    }

    public async Task Process(Guid id, CancellationToken ct)
    {
        var initial = await transfers.GetByIdAsync(id, ct);
        if (initial is null || initial.Status != "Pending")
        {
            return;
        }

        // The user is already disabled durably; avoid holding an Identity lock across service calls.
        var result = await remote.ApplyAsync(id, initial.UserId, initial.SourceRestaurantId, initial.TargetRestaurantId, ct);
        if (result.Status is not ("Completed" or "Rejected"))
        {
            return;
        }

        await work.ExecuteInTransactionAsync(async () =>
        {
            var user = await users.LockAsync(initial.UserId, ct);
            // Refresh tracked state when another worker completed this operation while waiting for the user lock.
            var current = await transfers.GetByIdAsync(id, ct);
            if (current is null || current.Status != "Pending" || user is null)
            {
                return false;
            }

            if (result.Status == "Completed")
            {
                user.RestaurantId = current.TargetRestaurantId;
            }

            user.IsActive = current.WasActive;
            user.UpdatedAt = DateTime.UtcNow;
            current.Status = result.Status;
            current.Message = result.Message;
            current.CompletedAt = DateTime.UtcNow;
            work.RecordAudit(current.RequestedBy, "STAFF_TRANSFER_" + result.Status.ToUpperInvariant(), "User", user.UserId.ToString(),
                new
                {
                    RestaurantId = current.SourceRestaurantId
                }, new
                {
                    user.RestaurantId,
                    TransferId = id,
                    result.Message
                });
            return true;
        }, ct);
    }

    private static ResponseDTO Response(StaffTransfer transfer)
    {
        var code = transfer.Status == "Pending" ? 202 : transfer.Status == "Rejected" ? 409 : 200;
        return new(transfer.Message ?? (code == 202 ? "Đang đồng bộ chuyển nhà hàng; nhân viên tạm thời không thể đăng nhập." : "Đã chuyển nhân viên sang nhà hàng mới."),
            code, code != 409, new StaffTransferDTO(transfer.StaffTransferId, transfer.UserId, transfer.SourceRestaurantId,
                transfer.TargetRestaurantId, transfer.Status, transfer.Message, transfer.CreatedAt, transfer.CompletedAt));
    }
}
