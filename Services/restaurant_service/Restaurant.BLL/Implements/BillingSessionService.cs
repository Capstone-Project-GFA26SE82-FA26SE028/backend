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
            RestaurantId = branches[0]
        };
    }
}
