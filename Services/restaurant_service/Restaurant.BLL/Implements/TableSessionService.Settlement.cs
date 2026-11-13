using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed partial class TableSessionService
{
    public async Task<TableSessionClosureDto> ClosePaidSession(Shared.Common.Billing.PaidSessionSettlement payment, CancellationToken ct)
    {
        var sessionId = payment.TableSessionId;
        if (sessionId == Guid.Empty || payment.RestaurantId == Guid.Empty || payment.BillId == Guid.Empty || payment.CheckoutId == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu chứng từ thanh toán hợp lệ.");
        }

        var initial = await sessions.GetForBillingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        var restaurantId = SessionRestaurant(initial);
        if (restaurantId != payment.RestaurantId)
        {
            throw new ManagementException(409, "Chứng từ thanh toán không thuộc nhà hàng của phiên bàn.");
        }

        if (initial.Status == "Closed" && initial.ClosedAt.HasValue)
        {
            return ClosureMap(initial, restaurantId);
        }

        if (initial.Status != "Open" || initial.ClosedAt.HasValue)
        {
            throw new ManagementException(409, "Trạng thái phiên bàn không cho phép kết thúc.");
        }

        var tableIds = initial.SessionTables.Where(link => !link.ReleasedAt.HasValue)
            .Select(link => link.TableId).Distinct().OrderBy(id => id).ToArray();
        if (tableIds.Length == 0)
        {
            throw new ManagementException(409, "Phiên bàn không có bàn đang được sử dụng.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            foreach (var tableId in tableIds)
            {
                _ = await tables.LockForSessionAsync(tableId, ct)
                    ?? throw new ManagementException(409, "Bàn của phiên phục vụ đã thay đổi.");
            }

            _ = await sessions.LockAsync(sessionId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
            var current = await sessions.GetForDepartureAsync(sessionId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
            if (SessionRestaurant(current) != restaurantId)
            {
                throw new ManagementException(403, "Nhà hàng của phiên bàn đã thay đổi.");
            }

            if (current.Status == "Closed" && current.ClosedAt.HasValue)
            {
                return ClosureMap(current, restaurantId);
            }

            var reviewed = await paymentRequests.GetPaymentRequestAsync(payment.CheckoutId, ct);
            if (reviewed is null || reviewed.TableSessionId != sessionId || reviewed.Status != "Reviewed")
            {
                throw new ManagementException(409, "Yêu cầu thanh toán đã thay đổi.");
            }

            var links = current.SessionTables.Where(link => !link.ReleasedAt.HasValue).ToArray();
            if (current.Status != "Open" || current.ClosedAt.HasValue
                || !tableIds.SequenceEqual(links.Select(link => link.TableId).Distinct().OrderBy(id => id)))
            {
                throw new ManagementException(409, "Bàn hoặc phiên phục vụ đã thay đổi. Vui lòng tải lại.");
            }

            foreach (var tableId in tableIds)
            {
                var occupying = await sessions.GetOpenForTableAsync(tableId, ct);
                if (occupying.Count != 1 || occupying[0].TableSessionId != sessionId)
                {
                    throw new ManagementException(409, "Bàn có phiên phục vụ xung đột. Không thể tự giải phóng bàn.");
                }
            }

            var now = DateTime.SpecifyKind(new System.Data.SqlTypes.SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
            current.Status = "Closed";
            current.ClosedAt = now;
            foreach (var link in links)
            {
                link.ReleasedAt = now;
            }

            foreach (var guest in await guests.GetUnrevokedForSessionAsync(sessionId, ct))
            {
                guest.RevokedAt = now;
            }

            return ClosureMap(current, restaurantId);
        }, ct);
    }

    public async Task<bool> TryClosePaidSession(Guid sessionId, CancellationToken ct)
    {
        var session = await sessions.GetForBillingAsync(sessionId, ct);
        if (session is null || session.ClosedAt.HasValue || session.Status != "Open")
        {
            return false;
        }

        var restaurantId = SessionRestaurant(session);
        var payment = await payments.GetPaidAsync(sessionId, restaurantId, ct);
        if (payment is null)
        {
            return false;
        }

        await ClosePaidSession(payment, ct);
        return true;
    }

    private static Guid SessionRestaurant(TableSession session)
    {
        var ids = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] == Guid.Empty)
        {
            throw new ManagementException(409, "Phiên bàn không xác định duy nhất một nhà hàng.");
        }

        return ids[0];
    }

    private static TableSessionClosureDto ClosureMap(TableSession session, Guid restaurantId)
    {
        return new TableSessionClosureDto
        {
            TableSessionId = session.TableSessionId,
            RestaurantId = restaurantId,
            Status = session.Status,
            ClosedAt = new DateTimeOffset(DateTime.SpecifyKind(session.ClosedAt!.Value, DateTimeKind.Utc)),
            TableIds = session.SessionTables.Where(link => link.ReleasedAt == session.ClosedAt)
                .Select(link => link.TableId).Distinct().OrderBy(id => id).ToArray()
        };
    }

}
