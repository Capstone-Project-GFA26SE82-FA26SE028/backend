using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed partial class TableSessionService
{
    public async Task<TableSessionDepartureDto> Depart(Guid sessionId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu mã phiên bàn hợp lệ.");
        }

        var initial = await sessions.GetForBillingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        var restaurantId = DepartureRestaurant(initial);
        var actor = await RequireDepartureWaiter(restaurantId, ct);
        if (initial.Status == "Closed" && initial.ClosedAt.HasValue)
        {
            return DepartureMap(initial, restaurantId);
        }

        if (initial.Status != "Open" || initial.ClosedAt.HasValue)
        {
            throw new ManagementException(409, "Trạng thái phiên bàn không cho phép kết thúc.");
        }

        // Finish the remote read before taking Restaurant locks; Billing checks Restaurant scope itself.
        await payments.RequirePaidAsync(sessionId, restaurantId, ct);
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
            if (await RequireDepartureWaiter(restaurantId, ct) != actor || DepartureRestaurant(current) != restaurantId)
            {
                throw new ManagementException(403, "Quyền nhân viên hoặc nhà hàng của phiên bàn đã thay đổi.");
            }

            if (current.Status == "Closed" && current.ClosedAt.HasValue)
            {
                return DepartureMap(current, restaurantId);
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
                link.ChangedByRef = actor;
                link.Table.CleaningStatus = "Dirty";
            }

            foreach (var guest in await guests.GetUnrevokedForSessionAsync(sessionId, ct))
            {
                guest.RevokedAt = now;
            }

            return DepartureMap(current, restaurantId);
        }, ct);
    }

    private async Task<Guid> RequireDepartureWaiter(Guid restaurantId, CancellationToken ct)
    {
        var actor = await scope.GetCurrentScope(restaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !settings.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new ManagementException(403, "Chỉ phục vụ được phân công tại nhà hàng mới được xác nhận khách rời bàn.");
        }

        return actor.UserId;
    }

    private static Guid DepartureRestaurant(TableSession session)
    {
        var ids = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
        if (ids.Length != 1 || ids[0] == Guid.Empty)
        {
            throw new ManagementException(409, "Phiên bàn không xác định duy nhất một nhà hàng.");
        }

        return ids[0];
    }

    private static TableSessionDepartureDto DepartureMap(TableSession session, Guid restaurantId)
    {
        return new TableSessionDepartureDto
        {
            TableSessionId = session.TableSessionId,
            RestaurantId = restaurantId,
            Status = session.Status,
            ClosedAt = new DateTimeOffset(DateTime.SpecifyKind(session.ClosedAt!.Value, DateTimeKind.Utc)),
            TableIds = session.SessionTables.Where(link => link.ReleasedAt == session.ClosedAt)
                .Select(link => link.TableId).Distinct().OrderBy(id => id).ToArray()
        };
    }

    private static void RequireClean(Table table)
    {
        if (table.CleaningStatus != "Clean")
        {
            throw new ManagementException(409, "Bàn chưa được xác nhận dọn xong. Chưa thể đón khách mới.");
        }
    }
}
