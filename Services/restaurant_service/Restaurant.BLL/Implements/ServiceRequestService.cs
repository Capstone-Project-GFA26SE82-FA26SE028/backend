using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class ServiceRequestService(IServiceRequestRepository requests, ITableSessionRepository sessions,
    IManagementTransaction transaction, ICurrentRestaurantScopeService scope, IGuestAccessService guests,
    IOptions<ServiceRequestSettings> settings, TimeProvider clock, IPaymentRequestOrderClient orders) : IServiceRequestService
{
    public async Task<ServiceRequestDto> Create(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        var restaurantId = await RequireSession(sessionId, guestToken, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            await LockSession(sessionId, ct);
            if (await RequireSession(sessionId, guestToken, ct) != restaurantId)
            {
                throw new ManagementException(409, "Nhà hàng của phiên bàn đã thay đổi. Vui lòng tải lại yêu cầu.");
            }

            var existing = await requests.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                CheckIdentity(existing, sessionId);
                return Map(existing, restaurantId);
            }

            if (await requests.GetActivePaymentRequestAsync(sessionId, ct) is not null)
            {
                throw new ManagementException(409, "Phiên bàn đã có yêu cầu thanh toán. Vui lòng tiếp tục yêu cầu hiện tại.");
            }

            var request = new ServiceRequest
            {
                ServiceRequestId = id,
                TableSessionId = sessionId,
                Type = "Payment",
                Status = "Requested",
                RequestedAt = clock.GetUtcNow().UtcDateTime
            };
            await requests.AddAsync(request);
            return Map(request, restaurantId);
        }, ct);
    }

    public async Task<ServiceRequestDto> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        var restaurantId = await RequireSession(sessionId, guestToken, ct);
        var request = await requests.GetPaymentRequestAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy yêu cầu thanh toán.");
        CheckIdentity(request, sessionId);
        return Map(request, restaurantId);
    }

    public async Task<PageResult<ServiceRequestDto>> GetAll(Guid restaurantId, string? status, int page, int pageSize, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty || page < 1 || pageSize is < 1 or > 100
            || (long)(page - 1) * pageSize > int.MaxValue
            || status is not (null or "Requested" or "Acknowledged" or "Reviewed" or "Cancelled"))
        {
            throw new ManagementException(400, "Vui lòng cung cấp nhà hàng, phân trang và trạng thái yêu cầu hợp lệ.");
        }

        await RequireStaff(restaurantId, settings.Value.WaiterRoleIds.Concat(settings.Value.CashierRoleIds), ct);
        var result = await requests.GetPageAsync(restaurantId, status, page, pageSize, ct);
        return new PageResult<ServiceRequestDto>
        {
            Items = result.Items.Select(item => Map(item, restaurantId)).ToList(),
            TotalCount = result.Total,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<ServiceRequestDto> Acknowledge(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Change(sessionId, id, "Acknowledged", ct);
    }

    public Task<ServiceRequestDto> Review(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Change(sessionId, id, "Reviewed", ct);
    }

    public Task<ServiceRequestDto> Cancel(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Change(sessionId, id, "Cancelled", ct);
    }

    private async Task<ServiceRequestDto> Change(Guid sessionId, Guid id, string status, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        var session = await Session(sessionId, ct);
        var restaurantId = RestaurantId(session);
        var allowedRoles = status == "Cancelled" ? settings.Value.CashierRoleIds : settings.Value.WaiterRoleIds;
        var actor = await RequireStaff(restaurantId, allowedRoles, ct);
        if (status == "Reviewed")
        {
            await orders.RequireFrozenAsync(sessionId, id, ct);
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await LockSession(sessionId, ct);
            if (await RequireStaff(restaurantId, allowedRoles, ct) != actor)
            {
                throw new ManagementException(401, "Phiên đăng nhập của nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            var request = await requests.GetPaymentRequestAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy yêu cầu thanh toán.");
            CheckIdentity(request, sessionId);
            if (status == "Cancelled")
            {
                request.Status = status;
                request.CompletedAt ??= clock.GetUtcNow().UtcDateTime;
                return Map(request, restaurantId);
            }

            if (request.Status == "Cancelled" || (request.HandledByRef.HasValue && request.HandledByRef != actor))
            {
                throw new ManagementException(409, "Yêu cầu đã hủy hoặc đang được nhân viên phục vụ khác xử lý.");
            }

            if (status == "Reviewed" && request.Status == "Requested")
            {
                throw new ManagementException(409, "Vui lòng tiếp nhận yêu cầu trước khi xác nhận đã kiểm tra.");
            }

            if (request.Status == "Reviewed")
            {
                return Map(request, restaurantId);
            }

            request.HandledByRef = actor;
            request.AcknowledgedAt ??= clock.GetUtcNow().UtcDateTime;
            request.Status = status;
            return Map(request, restaurantId);
        }, ct);
    }

    private async Task<Guid> RequireSession(Guid sessionId, string guestToken, CancellationToken ct)
    {
        var session = await Session(sessionId, ct);
        var restaurantId = RestaurantId(session);
        if (!string.IsNullOrEmpty(guestToken))
        {
            await guests.GetMe(sessionId, guestToken, ct);
        }
        else
        {
            await RequireStaff(restaurantId, settings.Value.WaiterRoleIds.Concat(settings.Value.CashierRoleIds), ct);
        }

        return restaurantId;
    }

    private async Task<Guid> RequireStaff(Guid restaurantId, IEnumerable<string> allowedRoles, CancellationToken ct)
    {
        var actor = await scope.GetCurrentScope(restaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !allowedRoles.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Yêu cầu quyền nhân viên phục vụ hoặc thu ngân được phân công tại nhà hàng.");
        }

        return actor.UserId;
    }

    private async Task<TableSession> Session(Guid id, CancellationToken ct)
    {
        var session = await sessions.GetForOrderingAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        if (session.ClosedAt.HasValue || session.Status != "Open")
        {
            throw new ManagementException(409, "Phiên bàn đã đóng.");
        }

        return session;
    }

    private async Task LockSession(Guid id, CancellationToken ct)
    {
        var session = await sessions.LockAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        if (session.ClosedAt.HasValue || session.Status != "Open")
        {
            throw new ManagementException(409, "Phiên bàn đã đóng.");
        }
    }

    private static Guid RestaurantId(TableSession session)
    {
        var ids = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
        if (ids.Length != 1)
        {
            throw new ManagementException(409, "Phiên bàn phải thuộc đúng một nhà hàng.");
        }

        return ids[0];
    }

    private static void CheckIdentity(ServiceRequest request, Guid sessionId)
    {
        if (request.TableSessionId != sessionId || request.Type != "Payment")
        {
            throw new ManagementException(409, "Mã yêu cầu đã được sử dụng cho nội dung khác.");
        }
    }

    private static void ValidateIds(Guid sessionId, Guid id)
    {
        if (sessionId == Guid.Empty || id == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu mã phiên bàn và mã yêu cầu thanh toán hợp lệ.");
        }
    }

    private static ServiceRequestDto Map(ServiceRequest request, Guid restaurantId)
    {
        return new ServiceRequestDto
        {
            ServiceRequestId = request.ServiceRequestId,
            TableSessionId = request.TableSessionId,
            RestaurantId = restaurantId,
            Type = request.Type,
            Status = request.Status,
            RequestedAt = DateTime.SpecifyKind(request.RequestedAt, DateTimeKind.Utc),
            AcknowledgedAt = request.AcknowledgedAt.HasValue ? DateTime.SpecifyKind(request.AcknowledgedAt.Value, DateTimeKind.Utc) : null,
            CompletedAt = request.CompletedAt.HasValue ? DateTime.SpecifyKind(request.CompletedAt.Value, DateTimeKind.Utc) : null,
            HandledByRef = request.HandledByRef
        };
    }
}
