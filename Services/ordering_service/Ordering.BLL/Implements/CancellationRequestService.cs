using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Shared.Common.Pagination;

namespace Ordering.BLL.Implements;

public sealed class CancellationRequestService(ICancellationRequestRepository repository,
    ICheckoutLockRepository checkouts, IOrderingTransaction transaction, IOrderingAccess access,
    ICancellationRequestStaffAccess staffAccess, TimeProvider clock) : ICancellationRequestService
{
    public async Task<(CancellationRequestDTO Result, bool Created)> Create(Guid sessionId, Guid id, Guid itemId,
        int quantity, string reason, string guestToken, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, guestToken, ct);
        if (!actor.IsGuest || !actor.GuestId.HasValue)
        {
            throw new OrderingException(403, "Chỉ khách trong phiên bàn được gửi yêu cầu hủy món.");
        }

        reason = ValidateReason(reason);
        if (id == Guid.Empty || itemId == Guid.Empty || quantity < 1)
        {
            throw new OrderingException(400, "Cần Idempotency-Key dạng UUID, mã món và số lượng hủy lớn hơn 0.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await checkouts.LockSessionAsync(sessionId, ct);
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                if (existing.Source != "Customer" || existing.OrderItem.Order.TableSessionRef != sessionId || existing.OrderItemId != itemId
                    || existing.RequestedByRef != actor.GuestId || existing.Quantity != quantity || existing.Reason != reason)
                {
                    throw new OrderingException(409, "Idempotency-Key đã dùng cho yêu cầu khác.");
                }

                return (Map(existing), false);
            }

            await RequireUnfrozen(sessionId, ct);
            var item = await FindItem(sessionId, itemId, ct);
            if (await repository.HasPendingAsync(itemId, ct))
            {
                throw new OrderingException(409, "Món này đã có yêu cầu hủy đang chờ xử lý.");
            }

            RequireCancellable(item, quantity);
            await access.RequireAsync(sessionId, null, guestToken, ct);
            var request = new CancellationRequest
            {
                CancellationRequestId = id,
                OrderItemId = itemId,
                OrderItem = item,
                Quantity = quantity,
                Reason = reason,
                RequestedByRef = actor.GuestId.Value,
                Status = "Pending",
                RequestedAt = clock.GetUtcNow().UtcDateTime
            };
            repository.Create(request);
            return (Map(request), true);
        }, ct);
    }

    public async Task<(CancellationRequestDTO Result, bool Created)> CancelByStaff(Guid sessionId, Guid id,
        Guid itemId, int quantity, string reason, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, "", ct);
        if (actor.IsGuest || !actor.UserId.HasValue)
        {
            throw new OrderingException(403, "Chỉ nhân viên phục vụ trong nhà hàng được chủ động hủy món.");
        }

        reason = ValidateReason(reason);
        if (id == Guid.Empty || itemId == Guid.Empty || quantity < 1)
        {
            throw new OrderingException(400, "Cần Idempotency-Key dạng UUID, mã món và số lượng hủy lớn hơn 0.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await checkouts.LockSessionAsync(sessionId, ct);
            var existing = await repository.GetByIdAsync(id, ct);
            if (existing is not null)
            {
                if (existing.Source != "Staff" || existing.OrderItem.Order.TableSessionRef != sessionId
                    || existing.OrderItemId != itemId || existing.RequestedByRef != actor.UserId
                    || existing.Quantity != quantity || existing.Reason != reason)
                {
                    throw new OrderingException(409, "Idempotency-Key đã dùng cho thao tác khác.");
                }

                return (Map(existing), false);
            }

            await RequireUnfrozen(sessionId, ct);
            var item = await FindItem(sessionId, itemId, ct);
            if (await repository.HasPendingAsync(itemId, ct))
            {
                throw new OrderingException(409, "Món đang có yêu cầu hủy của khách. Vui lòng duyệt hoặc từ chối yêu cầu đó trước.");
            }

            RequireCancellable(item, quantity);
            await access.RequireAsync(sessionId, null, "", ct);
            ApplyCancellation(item, quantity, actor.UserId.Value);
            var now = clock.GetUtcNow().UtcDateTime;
            var request = new CancellationRequest
            {
                CancellationRequestId = id,
                OrderItemId = itemId,
                OrderItem = item,
                Quantity = quantity,
                Reason = reason,
                Source = "Staff",
                RequestedByRef = actor.UserId.Value,
                DecidedByRef = actor.UserId.Value,
                Status = "Approved",
                RequestedAt = now,
                DecidedAt = now,
                DecisionReason = reason
            };
            repository.Create(request);
            return (Map(request), true);
        }, ct);
    }

    public async Task<CancellationRequestDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        await RequireRead(sessionId, guestToken, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            return Map(await Find(sessionId, id, ct));
        }, ct);
    }

    public async Task<PagedResult<CancellationRequestDTO>> GetAll(Guid? sessionId, string? status, int page,
        int pageSize, string guestToken, CancellationToken ct)
    {
        if (sessionId.HasValue)
        {
            await RequireRead(sessionId.Value, guestToken, ct);
        }
        else
        {
            await staffAccess.RequireAsync(ct);
        }

        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize
            || status is not (null or "Pending" or "Approved" or "Rejected"))
        {
            throw new OrderingException(400, "Phân trang hoặc trạng thái yêu cầu hủy không hợp lệ.");
        }

        var result = await repository.GetAllAsync(sessionId, status, page, pageSize, ct);
        return new PagedResult<CancellationRequestDTO>(result.Items.Select(Map).ToArray(), result.Total, page, pageSize);
    }

    public Task<CancellationRequestDTO> Approve(Guid sessionId, Guid id, CancellationToken ct)
    {
        return Decide(sessionId, id, true, null, ct);
    }

    public Task<CancellationRequestDTO> Reject(Guid sessionId, Guid id, string reason, CancellationToken ct)
    {
        return Decide(sessionId, id, false, ValidateReason(reason), ct);
    }

    private async Task<CancellationRequestDTO> Decide(Guid sessionId, Guid id, bool approve, string? reason, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, "", ct);
        if (actor.IsGuest || !actor.UserId.HasValue)
        {
            throw new OrderingException(403, "Chỉ nhân viên phục vụ được xử lý yêu cầu hủy món.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            await checkouts.LockSessionAsync(sessionId, ct);
            var request = await Find(sessionId, id, ct);
            var target = approve ? "Approved" : "Rejected";
            if (request.Status != "Pending")
            {
                if (request.Status == target && request.DecidedByRef == actor.UserId && request.DecisionReason == reason)
                {
                    return Map(request);
                }

                throw new OrderingException(409, "Yêu cầu đã được xử lý. Vui lòng tải lại.");
            }

            if (approve)
            {
                await RequireUnfrozen(sessionId, ct);
                var item = await FindItem(sessionId, request.OrderItemId, ct);
                RequireCancellable(item, request.Quantity);
                ApplyCancellation(item, request.Quantity, actor.UserId.Value);
            }

            await access.RequireAsync(sessionId, null, "", ct);
            request.Status = target;
            request.DecidedByRef = actor.UserId;
            request.DecidedAt = clock.GetUtcNow().UtcDateTime;
            request.DecisionReason = reason;
            return Map(request);
        }, ct);
    }

    private void ApplyCancellation(OrderItem item, int quantity, Guid actorId)
    {
        var remaining = quantity;
        foreach (var task in item.KitchenTasks.Where(task => task.Status is "Pending" or "Hold")
            .OrderByDescending(task => task.BatchNumber).ThenBy(task => task.KitchenTaskId))
        {
            if (remaining == 0)
            {
                break;
            }

            var reduction = Math.Min(remaining, task.Quantity);
            var from = task.Status;
            if (reduction == task.Quantity)
            {
                task.Status = "Cancelled";
            }
            else
            {
                task.Quantity -= reduction;
            }

            task.Events.Add(new Event
            {
                EventId = Guid.NewGuid(),
                KitchenTaskId = task.KitchenTaskId,
                FromStatus = from,
                ToStatus = task.Status,
                ActorUserRef = actorId,
                OccurredAt = clock.GetUtcNow().UtcDateTime
            });
            remaining -= reduction;
        }

        item.CancelledQuantity += quantity;
        if (item.CancelledQuantity == item.Quantity)
        {
            item.Status = "Cancelled";
        }

        if (item.Order.OrderItems.All(line => line.Status == "Cancelled"))
        {
            item.Order.Status = "Cancelled";
        }
        else if (item.Order.Status != "PendingConfirmation"
            && item.Order.OrderItems.All(line => line.Status is "Ready" or "Served" or "Cancelled"))
        {
            item.Order.Status = item.Order.OrderItems.Any(line => line.Status == "Ready") ? "Ready" : "Served";
        }
    }

    private async Task RequireRead(Guid sessionId, string guestToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(guestToken))
        {
            await access.RequireForServingAsync(sessionId, ct);
        }
        else
        {
            await access.RequireAsync(sessionId, null, guestToken, ct);
        }
    }

    private async Task RequireUnfrozen(Guid sessionId, CancellationToken ct)
    {
        if ((await checkouts.GetActiveAsync(sessionId, ct))?.Status == "Frozen")
        {
            throw new OrderingException(409, "Danh sách món đã chốt thanh toán; cần hủy yêu cầu thanh toán trước khi hủy món.");
        }
    }

    private async Task<CancellationRequest> Find(Guid sessionId, Guid id, CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(id, ct);
        if (request is null || request.OrderItem.Order.TableSessionRef != sessionId)
        {
            throw new OrderingException(404, "Không tìm thấy yêu cầu hủy trong phiên bàn này.");
        }

        return request;
    }

    private async Task<OrderItem> FindItem(Guid sessionId, Guid id, CancellationToken ct)
    {
        var item = await repository.GetItemAsync(id, ct);
        if (item is null || item.Order.TableSessionRef != sessionId || item.Order.CustomerUserRef.HasValue)
        {
            throw new OrderingException(404, "Không tìm thấy món trong phiên bàn này.");
        }

        return item;
    }

    private static void RequireCancellable(OrderItem item, int quantity)
    {
        var available = item.Order.Status == "PendingConfirmation" && item.Status == "PendingConfirmation"
            ? item.Quantity - item.CancelledQuantity
            : item.KitchenTasks.Where(task => task.Status is "Pending" or "Hold").Sum(task => task.Quantity);
        if (item.Order.Status is not ("PendingConfirmation" or "Confirmed" or "Ready")
            || item.Status is "Cancelled" or "Served"
            || quantity > item.Quantity - item.CancelledQuantity || quantity > available)
        {
            throw new OrderingException(409, "Chỉ được hủy số lượng chưa nấu và chưa hủy trước đó.");
        }
    }

    private static string ValidateReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 1000)
        {
            throw new OrderingException(400, "Vui lòng nhập lý do từ 1 đến 1000 ký tự.");
        }

        return text;
    }

    private static CancellationRequestDTO Map(CancellationRequest request)
    {
        return new CancellationRequestDTO
        {
            CancellationRequestId = request.CancellationRequestId,
            TableSessionId = request.OrderItem.Order.TableSessionRef!.Value,
            OrderId = request.OrderItem.OrderId,
            OrderNumber = request.OrderItem.Order.OrderNumber,
            OrderItemId = request.OrderItemId,
            ItemName = request.OrderItem.NameSnapshot,
            Quantity = request.Quantity,
            Reason = request.Reason,
            Source = request.Source,
            RequestedByRef = request.RequestedByRef,
            DecidedByRef = request.DecidedByRef,
            Status = request.Status,
            RequestedAt = request.RequestedAt,
            DecidedAt = request.DecidedAt,
            DecisionReason = request.DecisionReason
        };
    }
}
