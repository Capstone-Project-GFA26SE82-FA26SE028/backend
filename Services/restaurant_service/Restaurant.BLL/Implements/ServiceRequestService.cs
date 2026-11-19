using System.Data;
using System.Data.SqlTypes;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Constants;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class ServiceRequestService(IServiceRequestRepository requests, ITableSessionRepository sessions,
    IManagementTransaction transaction, IGuestAccessService guests, IStaffIdentity identity,
    IRestaurantStaffRepository staff, IOptions<ServiceRequestSettings> settings, TimeProvider clock,
    IPaymentRequestOrderClient orders) : IServiceRequestService
{
    public async Task<ServiceRequestDto> Create(Guid sessionId, Guid id, string guestToken, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        await guests.GetMe(sessionId, guestToken, ct);
        // The session lock serializes competing requests for this table through commit.
        return await transaction.ExecuteAsync(async () =>
        {
            var session = await LockSession(sessionId, ct);
            RequireOpenSession(session);
            await guests.GetMe(sessionId, guestToken, ct);
            var restaurantId = GetRestaurantId(session);

            var existing = await requests.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                if (existing.TableSessionId != sessionId || existing.Type != kind.ToString())
                {
                    throw new ManagementException(409, "Mã yêu cầu đã được sử dụng. Vui lòng dùng mã khác.");
                }

                return Map(existing, restaurantId, includeHandler: false);
            }

            if (await requests.GetPendingAsync(sessionId, kind.ToString(), ct) is not null)
            {
                throw new ManagementException(409, "Phiên bàn đã có yêu cầu cùng loại đang được xử lý.");
            }

            var request = new ServiceRequest
            {
                ServiceRequestId = id,
                TableSessionId = sessionId,
                Type = kind.ToString(),
                Status = PaymentRequestStatuses.Requested,
                RequestedAt = UtcNow()
            };
            requests.Add(request);
            await requests.EnqueueCreatedNotificationAsync(request, restaurantId, ct);
            return Map(request, restaurantId, includeHandler: false);
        }, ct, IsolationLevel.ReadCommitted);
    }

    public async Task<ServiceRequestDto> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        await guests.GetMe(sessionId, guestToken, ct);
        var restaurantId = await GetRestaurantId(sessionId, ct);
        return Map(await Find(sessionId, id, ct, kind), restaurantId, includeHandler: false);
    }

    public async Task<ServiceRequestDto> GetCurrent(Guid sessionId, string guestToken, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        await guests.GetMe(sessionId, guestToken, ct);
        var restaurantId = await GetRestaurantId(sessionId, ct);
        var request = await requests.GetPendingAsync(sessionId, kind.ToString(), ct)
            ?? throw new ManagementException(404, "Phiên bàn chưa có yêu cầu cùng loại đang xử lý.");
        return Map(request, restaurantId, includeHandler: false);
    }

    public async Task<PageResult<ServiceRequestDto>> GetAll(Guid restaurantId, int page, int pageSize, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new ManagementException(400, "Vui lòng cung cấp thông tin phân trang hợp lệ.");
        }

        var waiter = await GetWaiter(ct);
        await RequireAssignment(restaurantId, waiter, ct);
        var result = await requests.GetPageAsync(restaurantId, kind.ToString(), page, pageSize, ct);
        return new PageResult<ServiceRequestDto>
        {
            Items = result.Items.Select(request =>
                Map(request, restaurantId, includeHandler: true, tables: request.TableSession.SessionTables)).ToList(),
            TotalCount = result.Total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PaymentRequestReviewDto> GetReview(Guid sessionId, Guid id, CancellationToken ct)
    {
        var waiter = await GetWaiter(ct);
        var session = await GetSession(sessionId, ct);
        var restaurantId = GetRestaurantId(session);
        await RequireAssignment(restaurantId, waiter, ct);
        RequireOpenSession(session);

        var request = await Find(sessionId, id, ct);
        if (request.HandledByRef.HasValue && request.HandledByRef != waiter)
        {
            throw new ManagementException(403, "Chỉ nhân viên đang phụ trách yêu cầu mới được kiểm tra các món.");
        }

        if (request.CompletedAt.HasValue || request.Status != PaymentRequestStatuses.Acknowledged
            || request.HandledByRef != waiter)
        {
            throw new ManagementException(409, "Phải tiếp nhận yêu cầu trước khi kiểm tra các món.");
        }

        return await orders.GetReviewAsync(sessionId, restaurantId, ct);
    }

    public async Task<Guid> GetResultRestaurant(Guid sessionId, Guid id, CancellationToken ct)
    {
        var waiter = await GetWaiter(ct);
        var session = await sessions.GetForBillingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        var restaurantId = GetRestaurantId(session);
        await RequireAssignment(restaurantId, waiter, ct);
        var request = await Find(sessionId, id, ct);
        if (request.HandledByRef != waiter)
        {
            throw new ManagementException(403, "Chỉ phục vụ phụ trách yêu cầu được xem kết quả thanh toán.");
        }

        if (!(session.Status == "Open" && session.ClosedAt is null && request.Status == PaymentRequestStatuses.Acknowledged
                && request.CompletedAt is null)
            && !(session.Status == "Closed" && session.ClosedAt.HasValue && request.Status == PaymentRequestStatuses.Completed
                && request.CompletedAt.HasValue))
        {
            throw new ManagementException(409, "Yêu cầu chưa được tiếp nhận hoặc không còn hiệu lực.");
        }

        return restaurantId;
    }

    public async Task<ServiceRequestDto> Acknowledge(Guid sessionId, Guid id, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        var waiter = await GetWaiter(ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var session = await LockSession(sessionId, ct);
            var restaurantId = GetRestaurantId(session);
            var assignment = await staff.LockAsync(restaurantId, waiter, ct);
            if (assignment?.IsActive != true)
            {
                throw new ManagementException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
            }

            RequireOpenSession(session);
            var request = await Find(sessionId, id, ct, kind);
            if (request.CompletedAt is null && request.Status == PaymentRequestStatuses.Acknowledged
                && request.HandledByRef == waiter)
            {
                return Map(request, restaurantId, includeHandler: true, tables: session.SessionTables);
            }

            if (request.CompletedAt.HasValue || request.Status != PaymentRequestStatuses.Requested || request.HandledByRef.HasValue)
            {
                throw new ManagementException(409, "Yêu cầu không còn chờ tiếp nhận.");
            }

            request.Status = PaymentRequestStatuses.Acknowledged;
            request.HandledByRef = waiter;
            request.AcknowledgedAt = UtcNow();
            return Map(request, restaurantId, includeHandler: true, tables: session.SessionTables);
        }, ct, IsolationLevel.ReadCommitted);
    }

    public async Task<ServiceRequestDto> GetStaffCall(Guid restaurantId, Guid id, CancellationToken ct)
    {
        var waiter = await GetWaiter(ct);
        await RequireAssignment(restaurantId, waiter, ct);
        var request = await requests.GetByIdAsync(id, ct);
        if (request is null || request.Type != ServiceRequestKind.StaffCall.ToString())
        {
            throw new ManagementException(404, "Không tìm thấy yêu cầu gọi nhân viên.");
        }

        var session = await sessions.GetForBillingAsync(request.TableSessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        if (GetRestaurantId(session) != restaurantId)
        {
            throw new ManagementException(404, "Không tìm thấy yêu cầu gọi nhân viên tại nhà hàng này.");
        }

        return Map(request, restaurantId, includeHandler: true, tables: session.SessionTables);
    }

    public async Task<ServiceRequestDto> CompleteStaffCall(Guid sessionId, Guid id, CancellationToken ct)
    {
        var waiter = await GetWaiter(ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var session = await LockSession(sessionId, ct);
            var restaurantId = GetRestaurantId(session);
            var assignment = await staff.LockAsync(restaurantId, waiter, ct);
            if (assignment?.IsActive != true)
            {
                throw new ManagementException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
            }

            var request = await Find(sessionId, id, ct, ServiceRequestKind.StaffCall);
            if (request.HandledByRef != waiter)
            {
                throw new ManagementException(403, "Chỉ nhân viên đã tiếp nhận được hoàn thành yêu cầu.");
            }

            if (request.Status == PaymentRequestStatuses.Completed && request.CompletedAt.HasValue)
            {
                return Map(request, restaurantId, includeHandler: true, tables: session.SessionTables);
            }

            RequireOpenSession(session);
            if (request.Status != PaymentRequestStatuses.Acknowledged || request.CompletedAt.HasValue)
            {
                throw new ManagementException(409, "Yêu cầu phải được tiếp nhận trước khi hoàn thành.");
            }

            request.Status = PaymentRequestStatuses.Completed;
            request.CompletedAt = UtcNow();
            return Map(request, restaurantId, includeHandler: true, tables: session.SessionTables);
        }, ct, IsolationLevel.ReadCommitted);
    }

    private async Task<Guid> GetWaiter(CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !settings.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Chỉ nhân viên phục vụ được tiếp nhận yêu cầu.");
        }

        return actor.UserId;
    }

    private async Task RequireAssignment(Guid restaurantId, Guid waiter, CancellationToken ct)
    {
        if (!await staff.IsActiveAsync(restaurantId, waiter, ct))
        {
            throw new ManagementException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }
    }

    private async Task<ServiceRequest> Find(Guid sessionId, Guid id, CancellationToken ct, ServiceRequestKind kind = ServiceRequestKind.Payment)
    {
        var request = await requests.GetByIdAsync(id, ct);
        if (request is null || request.TableSessionId != sessionId || request.Type != kind.ToString())
        {
            throw new ManagementException(404, "Không tìm thấy yêu cầu đúng loại trong phiên bàn này.");
        }

        return request;
    }

    private async Task<TableSession> LockSession(Guid sessionId, CancellationToken ct)
    {
        return await sessions.LockWithActiveTablesAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
    }

    private static void RequireOpenSession(TableSession session)
    {
        if (session.ClosedAt.HasValue || session.Status != "Open")
        {
            throw new ManagementException(409, "Phiên bàn đã đóng.");
        }
    }

    private async Task<Guid> GetRestaurantId(Guid sessionId, CancellationToken ct)
    {
        return GetRestaurantId(await GetSession(sessionId, ct));
    }

    private async Task<TableSession> GetSession(Guid sessionId, CancellationToken ct)
    {
        return await sessions.GetForOrderingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
    }

    private static Guid GetRestaurantId(TableSession session)
    {
        var owners = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
        if (owners.Length != 1)
        {
            throw new ManagementException(409, "Phiên bàn phải thuộc đúng một nhà hàng.");
        }

        return owners[0];
    }

    private DateTime UtcNow()
    {
        // ServiceRequest uses SQL Server datetime, so retries return the persisted precision.
        return DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
    }

    private static ServiceRequestDto Map(ServiceRequest request, Guid restaurantId, bool includeHandler,
        IEnumerable<SessionTable>? tables = null)
    {
        return new ServiceRequestDto
        {
            ServiceRequestId = request.ServiceRequestId,
            TableSessionId = request.TableSessionId,
            RestaurantId = restaurantId,
            Status = request.Status,
            Type = request.Type,
            CompletedAt = request.CompletedAt is { } completedAt ? DateTime.SpecifyKind(completedAt, DateTimeKind.Utc) : null,
            RequestedAt = DateTime.SpecifyKind(request.RequestedAt, DateTimeKind.Utc),
            AcknowledgedAt = request.AcknowledgedAt is DateTime acknowledgedAt
                ? DateTime.SpecifyKind(acknowledgedAt, DateTimeKind.Utc) : null,
            HandledByRef = includeHandler ? request.HandledByRef : null,
            Tables = tables?.OrderBy(link => link.Table.TableCode, StringComparer.Ordinal)
                .ThenBy(link => link.TableId)
                .Select(link => new PaymentRequestTableDto(link.TableId, link.Table.TableCode))
                .ToArray() ?? []
        };
    }
}
