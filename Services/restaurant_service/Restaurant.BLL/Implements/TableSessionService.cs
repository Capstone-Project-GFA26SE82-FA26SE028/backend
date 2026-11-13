using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed partial class TableSessionService(IQrService qrService, ITableRepository tables,
    ITableSessionRepository sessions, IGenericRepository<SessionTable> sessionTables,
    IManagementTransaction transaction, IGuestAccessService guestAccess, ITableSessionOrderAccess orderAccess,
    ISessionPaymentClient payments, IGuestAccessRepository guests, IServiceRequestRepository paymentRequests, TimeProvider clock) : ITableSessionService
{
    public async Task<TableSessionScanDto> OpenForWaiter(Guid tableId, CancellationToken ct)
    {
        await orderAccess.RequireStaffAsync(ct);
        if (tableId == Guid.Empty)
        {
            throw new ManagementException(400, "Mã bàn không hợp lệ.");
        }
        return await transaction.ExecuteAsync(async () =>
        {
            var table = await tables.LockForSessionAsync(tableId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy bàn.");
            if (!table.IsActive || !table.Area.IsActive)
            {
                throw new ManagementException(409, "Bàn hoặc khu vực đang ngừng hoạt động.");
            }
            var open = await sessions.GetOpenForTableAsync(tableId, ct);
            if (open.Count > 1 || open.Any(session => session.Status != "Open"))
            {
                throw new ManagementException(409, "Bàn đang có phiên phục vụ xung đột. Vui lòng xử lý trước khi mở mới.");
            }
            var session = open.SingleOrDefault();
            var created = session is null;
            if (session is null)
            {
                var now = DateTime.UtcNow;
                session = new TableSession
                {
                    TableSessionId = Guid.NewGuid(),
                    PartySize = 1,
                    Status = "Open",
                    OpenedAt = now
                };
                sessions.Add(session);
                sessionTables.Add(new SessionTable
                {
                    SessionTableId = Guid.NewGuid(),
                    TableSessionId = session.TableSessionId,
                    TableId = tableId,
                    AssignedAt = now
                });
            }
            return new TableSessionScanDto
            {
                TableSessionId = session.TableSessionId,
                TableId = tableId,
                TableCode = table.TableCode,
                AreaId = table.AreaId,
                RestaurantId = table.Area.RestaurantId,
                Status = session.Status,
                OpenedAt = DateTime.SpecifyKind(session.OpenedAt, DateTimeKind.Utc),
                IsNewSession = created
            };
        }, ct);
    }

    public async Task<OrderSessionDto> GetForOrdering(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct)
    {
        var result = new OrderSessionDto
        {
            TableSessionId = sessionId
        };
        if (!string.IsNullOrEmpty(guestToken))
        {
            var guest = await guestAccess.GetMe(sessionId, guestToken, ct);
            result.GuestAccessId = guest.GuestAccessId;
            result.ExpiresAt = guest.ExpiresAt;
        }
        else
        {
            result.UserId = await orderAccess.RequireStaffAsync(ct);
        }

        var session = await sessions.GetForOrderingAsync(sessionId, ct)
            ?? throw new ManagementException(404, "Không tìm thấy phiên phục vụ bàn.");
        if (session.Status != "Open" || session.ClosedAt.HasValue)
        {
            throw new ManagementException(409, "Phiên phục vụ bàn đã đóng.");
        }

        var activeTables = session.SessionTables.Where(link => link.Table.IsActive && link.Table.Area.IsActive)
            .Select(link => link.TableId).Distinct().ToArray();
        if (activeTables.Length == 0 || (tableId.HasValue && !activeTables.Contains(tableId.Value)))
        {
            throw new ManagementException(409, "Bàn không hoạt động trong phiên phục vụ này.");
        }

        result.TableId = tableId ?? (activeTables.Length == 1 ? activeTables[0] : null);
        return result;
    }

    public async Task<TableSessionScanDto> Scan(string token, CancellationToken ct)
    {
        var initial = await qrService.Resolve(token, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var table = await tables.LockForSessionAsync(initial.TableId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy bàn.");
            // Validate again under the transaction: QR/area/table may have changed while waiting.
            var resolved = await qrService.Resolve(token, ct);
            if (resolved.TableId != table.TableId)
            {
                throw new ManagementException(409, "Mã QR đã thay đổi. Vui lòng quét lại.");
            }

            var open = await sessions.GetOpenForTableAsync(table.TableId, ct);
            if (open.Count > 1)
            {
                throw new ManagementException(409, "Bàn đang có nhiều phiên phục vụ chưa đóng. Vui lòng liên hệ nhân viên để được hỗ trợ.");
            }

            var created = open.Count == 0;
            var session = open.SingleOrDefault();
            if (session is null)
            {
                var now = DateTime.UtcNow;
                session = new TableSession
                {
                    TableSessionId = Guid.NewGuid(),
                    PartySize = 1,
                    Status = "Open",
                    OpenedAt = now
                };
                sessions.Add(session);
                sessionTables.Add(new SessionTable
                {
                    SessionTableId = Guid.NewGuid(),
                    TableSessionId = session.TableSessionId,
                    TableId = table.TableId,
                    AssignedAt = now
                });
            }

            return new TableSessionScanDto
            {
                TableSessionId = session.TableSessionId,
                TableId = resolved.TableId,
                TableCode = resolved.TableCode,
                AreaId = resolved.AreaId,
                RestaurantId = resolved.RestaurantId,
                Status = session.Status,
                OpenedAt = DateTime.SpecifyKind(session.OpenedAt, DateTimeKind.Utc),
                IsNewSession = created
            };
        }, ct);
    }
}
