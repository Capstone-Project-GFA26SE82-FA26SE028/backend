using System.Data.SqlTypes;
using Restaurant.Common.Constants;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.BLL.Implements;

public sealed partial class TableSessionService
{
    public async Task<OrderSessionDto> GetForServing(Guid sessionId, CancellationToken ct)
    {
        var userId = await orderAccess.RequireStaffAsync(ct);
        await dataScope.Resolve(null, sessionId, string.Empty, false, ct);

        var session = await sessions.GetForBillingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên phục vụ bàn.");
        Guid? paidCheckoutId = null;
        if (session.Status == "Closed" && session.ClosedAt.HasValue)
        {
            paidCheckoutId = await requests.GetClosingPaymentIdAsync(sessionId, session.ClosedAt.Value, ct)
                ?? throw new ManagementException(409, "Phiên bàn đã đóng mà chưa xác minh được hóa đơn đã thanh toán.");
        }
        else if (session.Status != "Open" || session.ClosedAt.HasValue || !session.SessionTables.Any(link =>
            !link.ReleasedAt.HasValue && link.Table.IsActive && link.Table.Area.IsActive))
        {
            throw new ManagementException(409, "Phiên hoặc bàn không hoạt động để phục vụ món.");
        }

        return new OrderSessionDto
        {
            TableSessionId = sessionId,
            UserId = userId,
            PaidCheckoutId = paidCheckoutId
        };
    }

    public async Task<bool> CloseAfterPayment(Guid restaurantId, Guid sessionId, CancellationToken ct)
    {
        var initial = await sessions.GetForBillingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        if (initial.SessionTables.Count == 0 || initial.SessionTables.Any(link => link.Table.Area.RestaurantId != restaurantId))
        {
            throw new ManagementException(403, "Phiên bàn không thuộc nhà hàng được yêu cầu.");
        }

        if (initial.Status == "Closed" && initial.ClosedAt.HasValue)
        {
            return true;
        }

        var tableIds = initial.SessionTables.Where(link => !link.ReleasedAt.HasValue)
            .Select(link => link.TableId).Distinct().Order().ToArray();
        if (initial.Status != "Open" || initial.ClosedAt.HasValue || tableIds.Length == 0)
        {
            throw new ManagementException(409, "Phiên bàn không thể tự động đóng.");
        }

        var checkoutId = await payments.GetCheckoutAsync(restaurantId, sessionId, ct);
        if (!checkoutId.HasValue)
        {
            return false;
        }

        return await transaction.ExecuteAsync(async () =>
        {
            // Scanning and opening also acquire the table lock before touching the session.
            foreach (var tableId in tableIds)
            {
                var table = await tables.LockForSessionAsync(tableId, ct);
                if (table?.Area.RestaurantId != restaurantId)
                {
                    throw new ManagementException(409, "Bàn của phiên phục vụ đã thay đổi.");
                }
            }

            var current = await sessions.LockAsync(sessionId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
            if (current.Status == "Closed" && current.ClosedAt.HasValue)
            {
                return true;
            }

            var activeLinks = await sessions.GetUnreleasedLinksAsync(sessionId, ct);
            if (current.Status != "Open" || current.ClosedAt.HasValue
                || !tableIds.SequenceEqual(activeLinks.Select(link => link.TableId).Distinct().Order()))
            {
                throw new ManagementException(409, "Bàn hoặc phiên phục vụ đã thay đổi. Vui lòng thử lại.");
            }

            var request = await requests.GetByIdAsync(checkoutId.Value, ct);
            if (request is null || request.TableSessionId != sessionId || request.Type != PaymentRequestStatuses.Type
                || request.Status != PaymentRequestStatuses.Acknowledged || !request.HandledByRef.HasValue
                || !request.AcknowledgedAt.HasValue || request.CompletedAt.HasValue)
            {
                throw new ManagementException(409, "Yêu cầu thanh toán không khớp với phiên bàn cần đóng.");
            }

            foreach (var tableId in tableIds)
            {
                var occupying = await sessions.GetOpenForTableAsync(tableId, ct);
                if (occupying.Count != 1 || occupying[0].TableSessionId != sessionId)
                {
                    throw new ManagementException(409, "Bàn đang có phiên phục vụ xung đột.");
                }
            }

            var now = new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value;
            current.Status = "Closed";
            current.ClosedAt = now;
            request.Status = PaymentRequestStatuses.Completed;
            request.CompletedAt = now;
            await requests.CancelPendingStaffCallsAsync(sessionId, now, ct);
            foreach (var link in activeLinks)
            {
                link.ReleasedAt = now;
            }

            foreach (var guest in await guests.GetUnrevokedBySessionAsync(sessionId, ct))
            {
                guest.RevokedAt = now;
            }

            return true;
        }, ct);
    }
}
