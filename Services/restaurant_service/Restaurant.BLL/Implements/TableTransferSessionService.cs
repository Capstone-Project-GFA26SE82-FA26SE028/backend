using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Common.TableTransfers;
using Shared.Persistence.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class TableTransferSessionService(ITableTransferSessionRepository operations,
    ITableSessionRepository sessions, ITableRepository tables, IGenericRepository<SessionTable> links,
    ITableSessionOrderAccess access, IRestaurantDataScopeService scope, IManagementTransaction transaction,
    IServiceRequestRepository requests, TimeProvider clock) : ITableTransferSessionService
{
    public async Task<TableTransferResultDTO> Prepare(TableTransferDTO request, CancellationToken ct)
    {
        Validate(request);
        var actor = await access.RequireStaffAsync(ct);
        var restaurant = await scope.Resolve(null, request.SourceSessionId, "", false, ct);
        var hash = Hash(request);
        return await transaction.ExecuteAsync(async () =>
        {
            await operations.LockAsync(request.OperationId, ct);
            var existing = await operations.GetAsync(request.OperationId, ct);
            if (existing is not null)
            {
                if (existing.ActorId != actor || existing.RestaurantId != restaurant || existing.RequestHash != hash)
                {
                    throw new ManagementException(409, "Mã thao tác đã được dùng với dữ liệu khác.");
                }
                return Map(existing);
            }

            var targetTable = await tables.LockForSessionAsync(request.TargetTableId, ct)
                ?? throw new ManagementException(404, "Không tìm thấy bàn đích.");
            if (targetTable.Area.RestaurantId != restaurant)
            {
                throw new ManagementException(403, "Không được tách/gộp bàn khác nhà hàng.");
            }
            if (!targetTable.IsActive || !targetTable.Area.IsActive)
            {
                throw new ManagementException(409, "Bàn đích hoặc khu vực đang ngừng hoạt động.");
            }
            var targetSessions = await sessions.GetOpenForTableAsync(request.TargetTableId, ct);
            if (targetSessions.Count > 1)
            {
                throw new ManagementException(409, "Bàn đích có nhiều phiên đang mở.");
            }
            var target = targetSessions.SingleOrDefault();
            if (target?.TableSessionId == request.SourceSessionId)
            {
                var sourceLinks = await sessions.GetUnreleasedLinksAsync(request.SourceSessionId, ct);
                if (request.Mode != "Split" || sourceLinks.Select(value => value.TableId).Distinct().Count() < 2)
                {
                    throw new ManagementException(409, "Chỉ được tách bàn trong cùng phiên khi phiên đang gộp từ ít nhất hai bàn.");
                }
                target = null;
            }
            var targetCreated = target is null;
            if (request.Mode == "Merge" && targetCreated)
            {
                throw new ManagementException(409, "Gộp bàn cần chọn một bàn đang có phiên phục vụ.");
            }

            var ids = new[] { request.SourceSessionId, target?.TableSessionId ?? Guid.Empty }
                .Where(value => value != Guid.Empty).Order().ToArray();
            TableSession? source = null;
            foreach (var id in ids)
            {
                var locked = await sessions.LockForTransferAsync(id, ct)
                    ?? throw new ManagementException(404, "Không tìm thấy phiên bàn.");
                if (locked.Status != "Open" || locked.ClosedAt.HasValue || await requests.HasPendingPaymentAsync(id, ct))
                {
                    throw new ManagementException(409, "Phiên bàn đang đóng, thanh toán hoặc đang tách/gộp.");
                }
                if (id == request.SourceSessionId)
                {
                    source = locked;
                }
                else
                {
                    target = locked;
                }
            }
            if (targetCreated)
            {
                target = new TableSession
                {
                    TableSessionId = Guid.NewGuid(),
                    PartySize = 1,
                    Status = "Rearranging",
                    OpenedAt = clock.GetUtcNow().UtcDateTime,
                    OpenedByRef = actor
                };
                sessions.Add(target);
                links.Add(new SessionTable
                {
                    SessionTableId = Guid.NewGuid(),
                    TableSessionId = target.TableSessionId,
                    TableId = request.TargetTableId,
                    AssignedAt = clock.GetUtcNow().UtcDateTime,
                    ChangedByRef = actor,
                    MoveReason = request.Reason
                });
            }
            source!.Status = "Rearranging";
            target!.Status = "Rearranging";
            var operation = new TableTransferSession
            {
                OperationId = request.OperationId,
                SourceSessionId = request.SourceSessionId,
                TargetSessionId = target.TableSessionId,
                TargetTableId = request.TargetTableId,
                ActorId = actor,
                RestaurantId = restaurant,
                RequestHash = hash,
                Mode = request.Mode,
                Reason = request.Reason,
                Status = "Prepared",
                TargetCreated = targetCreated,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            operations.Add(operation);
            return Map(operation);
        }, ct);
    }

    public async Task<TableTransferResultDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.RequireStaffAsync(ct);
        var value = await operations.GetAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy thao tác tách/gộp bàn.");
        await scope.Resolve(value.RestaurantId, null, "", false, ct);
        if (actor != value.ActorId)
        {
            throw new ManagementException(403, "Chỉ nhân viên tạo thao tác được tiếp tục thao tác này.");
        }
        return Map(value);
    }

    public async Task<TableTransferResultDTO> Finish(Guid id, bool abort, CancellationToken ct)
    {
        await GetById(id, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            await operations.LockAsync(id, ct);
            var operation = (await operations.GetAsync(id, ct))!;
            if (operation.Status != "Prepared")
            {
                return Map(operation);
            }
            var locked = new Dictionary<Guid, TableSession>();
            foreach (var sessionId in new[] { operation.SourceSessionId, operation.TargetSessionId }.Order())
            {
                locked[sessionId] = await sessions.LockForTransferAsync(sessionId, ct)
                    ?? throw new ManagementException(409, "Phiên bàn của thao tác không còn tồn tại.");
            }
            var source = locked[operation.SourceSessionId];
            var target = locked[operation.TargetSessionId];
            var now = clock.GetUtcNow().UtcDateTime;
            if (source.Status != "Rearranging" || target.Status != "Rearranging")
            {
                throw new ManagementException(409, "Trạng thái phiên bàn đã thay đổi.");
            }
            source.Status = "Open";
            target.Status = "Open";
            if (abort && operation.TargetCreated)
            {
                target.Status = "Closed";
                target.ClosedAt = now;
                foreach (var link in await sessions.GetUnreleasedLinksAsync(target.TableSessionId, ct))
                {
                    link.ReleasedAt = now;
                }
            }
            if (!abort && operation.Mode == "Split")
            {
                foreach (var link in (await sessions.GetUnreleasedLinksAsync(source.TableSessionId, ct))
                    .Where(value => value.TableId == operation.TargetTableId))
                {
                    link.ReleasedAt = now;
                    link.ChangedByRef = operation.ActorId;
                    link.MoveReason = operation.Reason;
                }
            }
            if (!abort && operation.Mode == "Merge")
            {
                foreach (var link in await sessions.GetUnreleasedLinksAsync(source.TableSessionId, ct))
                {
                    link.ReleasedAt = now;
                    links.Add(new SessionTable
                    {
                        SessionTableId = Guid.NewGuid(),
                        TableSessionId = target.TableSessionId,
                        TableId = link.TableId,
                        AssignedAt = now,
                        ChangedByRef = operation.ActorId,
                        MoveReason = operation.Reason
                    });
                }
                source.Status = "Closed";
                source.ClosedAt = now;
                target.PartySize += source.PartySize;
                await operations.RevokeGuestsAsync(source.TableSessionId, now, ct);
                await requests.CancelPendingStaffCallsAsync(source.TableSessionId, now, ct);
            }
            operation.Status = abort ? "Aborted" : "Completed";
            return Map(operation);
        }, ct);
    }

    private static void Validate(TableTransferDTO request)
    {
        if (request.OperationId == Guid.Empty || request.SourceSessionId == Guid.Empty || request.TargetTableId == Guid.Empty
            || request.Mode is not ("Split" or "Merge") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || request.Items is null || request.Items.Count > 100
            || request.Items.Any(value => value is null || value.OrderItemId == Guid.Empty || value.Quantity <= 0)
            || request.Items.Select(value => value.OrderItemId).Distinct().Count() != request.Items.Count
            || (request.Mode == "Split" && request.Items.Count == 0)
            || (request.Mode == "Merge" && request.Items.Count != 0))
        {
            throw new ManagementException(400, "Cần mã thao tác, phiên nguồn, bàn đích, lý do và danh sách món/số lượng tách hợp lệ.");
        }
    }

    public static string Hash(TableTransferDTO request)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }

    private static TableTransferResultDTO Map(TableTransferSession value)
    {
        return new TableTransferResultDTO
        {
            OperationId = value.OperationId,
            SourceSessionId = value.SourceSessionId,
            TargetSessionId = value.TargetSessionId,
            TargetTableId = value.TargetTableId,
            RestaurantId = value.RestaurantId,
            ActorId = value.ActorId,
            Status = value.Status
        };
    }
}
