using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class BillingSessionService(ICurrentRestaurantScopeService access,
    ITableSessionRepository sessions, IOptions<BillingReadSettings> settings) : IBillingSessionService
{
    public async Task<IReadOnlyList<BillingSessionDisplayDTO>> GetDisplays(Guid restaurantId,
        IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty || ids.Count is < 1 or > 200 || ids.Any(id => id == Guid.Empty))
        {
            throw new ManagementException(400, "Yêu cầu nhà hàng và từ 1 đến 200 mã phiên bàn hợp lệ.");
        }

        var actor = await access.GetCurrentScope(restaurantId, ct);
        if (!actor.IsAdministrator && !settings.Value.ReaderRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Yêu cầu quyền xem thông tin thanh toán.");
        }

        var distinctIds = ids.Distinct().ToArray();
        var found = await sessions.GetBillingDisplaysAsync(restaurantId, distinctIds, ct);
        if (found.Count != distinctIds.Length)
        {
            throw new ManagementException(404, "Không tìm thấy đầy đủ phiên bàn thuộc nhà hàng này.");
        }

        return found.Select(session =>
        {
            var links = session.SessionTables.Where(link => link.ReleasedAt == null).ToArray();
            if (links.Length == 0)
            {
                var lastRelease = session.SessionTables.Max(link => link.ReleasedAt);
                links = session.SessionTables.Where(link => link.ReleasedAt == lastRelease).ToArray();
            }

            var tables = links.Select(link => new BillingTableDisplayDTO(link.TableId, link.Table.TableCode,
                link.Table.AreaId, link.Table.Area.AreaName)).DistinctBy(table => table.TableId)
                .OrderBy(table => table.AreaName).ThenBy(table => table.TableCode).ThenBy(table => table.TableId).ToArray();
            return new BillingSessionDisplayDTO(session.TableSessionId, restaurantId, session.PartySize,
                new DateTimeOffset(DateTime.SpecifyKind(session.OpenedAt, DateTimeKind.Utc)), tables);
        }).ToArray();
    }

    public async Task<BillingSessionDto> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScope(null, ct);
        if (!actor.IsAdministrator && !settings.Value.ReaderRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new ManagementException(403, "Yêu cầu quyền xem thông tin thanh toán.");
        }

        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu ID phiên bàn hợp lệ.");
        }

        var session = await sessions.GetForBillingAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
        var branches = session.SessionTables.Select(link => link.Table.Area.RestaurantId).Distinct().ToArray();
        if (branches.Length != 1)
        {
            throw new ManagementException(409, "Phiên bàn không xác định duy nhất một nhà hàng.");
        }

        await access.GetCurrentScope(branches[0], ct);
        if (session.ClosedAt.HasValue || session.Status != "Open")
        {
            throw new ManagementException(409, "Phiên bàn chưa được mở hoặc đã đóng.");
        }

        return new BillingSessionDto
        {
            TableSessionId = id,
            RestaurantId = branches[0],
            CustomerRef = session.LoyaltyCustomerRef
        };
    }
}
