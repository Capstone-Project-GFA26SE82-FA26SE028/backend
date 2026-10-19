using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class TableSessionService(IQrService qrService, ITableRepository tables,
    ITableSessionRepository sessions, IGenericRepository<SessionTable> sessionTables,
    IManagementTransaction transaction) : ITableSessionService
{
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
                throw new ManagementException(409, "QR đã thay đổi. Hãy quét lại.");
            }

            var open = await sessions.GetOpenForTableAsync(table.TableId, ct);
            if (open.Count > 1)
            {
                throw new ManagementException(409, "Bàn có nhiều phiên chưa đóng. Liên hệ nhân viên kiểm tra.");
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
