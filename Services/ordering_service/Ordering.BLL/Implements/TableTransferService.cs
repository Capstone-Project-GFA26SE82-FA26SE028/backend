using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Shared.Common.TableTransfers;

namespace Ordering.BLL.Implements;

public sealed class TableTransferService(ITableTransferRepository operations, IOrderRepository orders,
    ICheckoutLockRepository checkouts, IOrderingTransaction transaction, ITableTransferSessionClient sessions,
    TimeProvider clock) : ITableTransferService
{
    public async Task<TableTransferResultDTO> GetById(Guid id, CancellationToken ct)
    {
        var result = await sessions.GetAsync(id, ct);
        var local = await operations.GetAsync(id, ct);
        if (local?.Status == "Applied")
        {
            result.Status = "Processing";
        }
        return result;
    }

    public async Task<TableTransferResultDTO> Execute(TableTransferDTO request, CancellationToken ct)
    {
        var prepared = await sessions.PrepareAsync(request, ct);
        if (prepared.Status == "Aborted")
        {
            throw new OrderingException(409, "Thao tác đã hủy. Vui lòng tạo mã thao tác mới.");
        }

        // Restaurant persists its reservation first. It remains blocked after a lost reply;
        // retrying the same operation safely resumes each committed step.
        try
        {
            if (prepared.Status == "Prepared" && await operations.GetAsync(request.OperationId, ct) is null
                && !await sessions.CanTransferAsync(prepared.SourceSessionId, prepared.TargetSessionId, ct))
            {
                throw new OrderingException(409, "Phiên nguồn hoặc đích đã có hóa đơn/yêu cầu thanh toán. Không thể tách/gộp.");
            }
            await transaction.ExecuteAsync(async () =>
            {
                await operations.LockSessionsAsync(prepared.SourceSessionId, prepared.TargetSessionId, ct);
                var existing = await operations.GetAsync(request.OperationId, ct);
                if (existing is not null)
                {
                    if (existing.Status == "Rejected")
                    {
                        throw new OrderingException(409, "Thao tác đã bị từ chối. Vui lòng tạo mã mới.");
                    }
                    return true;
                }
                if (prepared.Status != "Prepared")
                {
                    throw new OrderingException(409, "Không tìm thấy dữ liệu chuyển món của thao tác.");
                }
                if (await operations.HasPendingAsync(prepared.SourceSessionId, prepared.TargetSessionId, ct)
                    || await checkouts.GetActiveAsync(prepared.SourceSessionId, ct) is not null
                    || await checkouts.GetActiveAsync(prepared.TargetSessionId, ct) is not null)
                {
                    throw new OrderingException(409, "Phiên bàn đang chốt thanh toán hoặc đang chuyển món. Hãy xử lý yêu cầu đang có trước.");
                }

                var source = await orders.GetForTransferAsync(prepared.SourceSessionId, ct);
                var target = await orders.GetForTransferAsync(prepared.TargetSessionId, ct);
                if (source.Concat(target).Any(order => order.CustomerUserRef.HasValue || order.Channel != "DineIn"
                    || order.PaymentStatus is "Paid" or "RefundPending" or "Refunded")
                    || source.Concat(target).SelectMany(order => order.OrderItems)
                        .SelectMany(item => item.CancellationRequests).Any(value => value.Status == "Pending"))
                {
                    throw new OrderingException(409, "Chỉ được chuyển món tại bàn chưa thanh toán và không còn yêu cầu hủy đang chờ.");
                }
                if (request.Mode == "Merge")
                {
                    foreach (var order in source)
                    {
                        order.TableSessionRef = prepared.TargetSessionId;
                    }
                }
                else
                {
                    Split(request, prepared, source);
                }
                operations.Add(new TableTransfer
                {
                    OperationId = request.OperationId,
                    SourceSessionId = prepared.SourceSessionId,
                    TargetSessionId = prepared.TargetSessionId,
                    TargetTableId = prepared.TargetTableId,
                    ActorId = prepared.ActorId,
                    RestaurantId = prepared.RestaurantId,
                    RequestHash = Hash(request),
                    Mode = request.Mode,
                    Reason = request.Reason,
                    Status = "Applied",
                    CreatedAt = clock.GetUtcNow().UtcDateTime
                });
                return true;
            }, ct);
        }
        catch (OrderingException exception) when (exception.StatusCode is 400 or 403 or 404 or 409)
        {
            // Only a definite business rejection may release Restaurant's reservation.
            // Transport/database failures remain resumable rather than guessing whether they committed.
            operations.ResetTracking();
            var rejected = await transaction.ExecuteAsync(async () =>
            {
                await operations.LockSessionsAsync(prepared.SourceSessionId, prepared.TargetSessionId, ct);
                var existing = await operations.GetAsync(request.OperationId, ct);
                if (existing is not null)
                {
                    return existing.Status == "Rejected";
                }
                operations.Add(new TableTransfer
                {
                    OperationId = request.OperationId,
                    SourceSessionId = prepared.SourceSessionId,
                    TargetSessionId = prepared.TargetSessionId,
                    TargetTableId = prepared.TargetTableId,
                    ActorId = prepared.ActorId,
                    RestaurantId = prepared.RestaurantId,
                    Mode = request.Mode,
                    Reason = request.Reason,
                    Status = "Rejected",
                    RequestHash = Hash(request),
                    CreatedAt = clock.GetUtcNow().UtcDateTime
                });
                return true;
            }, ct);
            if (rejected)
            {
                await sessions.FinishAsync(request.OperationId, true, ct);
                throw;
            }
        }

        var completed = await sessions.FinishAsync(request.OperationId, false, ct);
        if (completed.Status != "Completed")
        {
            throw new OrderingException(409, "Thao tác chuyển món chưa hoàn tất. Vui lòng gửi lại cùng mã thao tác.");
        }
        await transaction.ExecuteAsync(async () =>
        {
            await operations.LockSessionsAsync(prepared.SourceSessionId, prepared.TargetSessionId, ct);
            var operation = (await operations.GetAsync(request.OperationId, ct))!;
            operation.Status = "Completed";
            return true;
        }, ct);
        return completed;
    }

    private void Split(TableTransferDTO request, TableTransferResultDTO prepared, List<Order> source)
    {
        var all = source.SelectMany(order => order.OrderItems).ToDictionary(item => item.OrderItemId);
        foreach (var selection in request.Items)
        {
            if (!all.TryGetValue(selection.OrderItemId, out var item)
                || selection.Quantity <= 0 || selection.Quantity > item.Quantity - item.CancelledQuantity
                || item.Status == "Cancelled")
            {
                throw new OrderingException(409, "Món/số lượng tách không thuộc phần còn lại của phiên nguồn.");
            }
            if (selection.Quantity < item.Quantity - item.CancelledQuantity
                && (item.Status is not ("PendingConfirmation" or "Pending" or "Hold")
                    || item.KitchenTasks.Any(task => task.Status is not ("Pending" or "Hold" or "Cancelled"))))
            {
                throw new OrderingException(409, "Món đã bắt đầu nấu chỉ được chuyển toàn bộ dòng món để giữ nguyên lịch sử chế biến.");
            }
        }

        foreach (var original in source)
        {
            var selected = request.Items.Where(value => original.OrderItems.Any(item => item.OrderItemId == value.OrderItemId)).ToArray();
            if (selected.Length == 0)
            {
                continue;
            }
            var moved = new Order
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = $"TR-{Guid.NewGuid():N}",
                RestaurantRef = prepared.RestaurantId,
                TableSessionRef = prepared.TargetSessionId,
                Channel = "DineIn",
                CreatedByRef = prepared.ActorId,
                CreatedAt = original.CreatedAt,
                ConfirmedAt = original.ConfirmedAt,
                ConfirmedByRef = original.ConfirmedByRef,
                IdempotencyKey = $"Transfer:{request.OperationId:N}:{original.OrderId:N}",
                RequestHash = original.RequestHash,
                Status = original.Status
            };
            foreach (var selection in selected)
            {
                var item = all[selection.OrderItemId];
                if (selection.Quantity == item.Quantity - item.CancelledQuantity)
                {
                    original.OrderItems.Remove(item);
                    moved.OrderItems.Add(item);
                    item.Order = moved;
                    item.OrderId = moved.OrderId;
                }
                else
                {
                    var split = SplitItem(item, selection.Quantity);
                    split.Order = moved;
                    split.OrderId = moved.OrderId;
                    moved.OrderItems.Add(split);
                }
            }
            UpdateStatus(original);
            UpdateStatus(moved);
            orders.Create(moved);
        }
    }

    private static OrderItem SplitItem(OrderItem item, int quantity)
    {
        var split = new OrderItem
        {
            OrderItemId = Guid.NewGuid(),
            ItemRef = item.ItemRef,
            RecipeVersionRef = item.RecipeVersionRef,
            OriginalTableRef = item.OriginalTableRef,
            ComboGroupId = item.ComboGroupId,
            NameSnapshot = item.NameSnapshot,
            UnitPriceSnapshot = item.UnitPriceSnapshot,
            Quantity = quantity,
            Note = item.Note,
            Status = item.Status
        };
        item.Quantity -= quantity;
        foreach (var option in item.OrderItemOptions)
        {
            split.OrderItemOptions.Add(new OrderItemOption
            {
                Id = Guid.NewGuid(),
                OrderItemId = split.OrderItemId,
                OptionRef = option.OptionRef,
                NameSnapshot = option.NameSnapshot,
                ExtraPriceSnapshot = option.ExtraPriceSnapshot,
                Quantity = option.Quantity
            });
        }
        var remaining = quantity;
        foreach (var task in item.KitchenTasks.Where(value => value.Status is "Pending" or "Hold").OrderBy(value => value.KitchenTaskId).ToArray())
        {
            if (remaining == 0)
            {
                break;
            }
            var movedQuantity = Math.Min(remaining, task.Quantity);
            if (movedQuantity == task.Quantity)
            {
                item.KitchenTasks.Remove(task);
                task.OrderItem = split;
                task.OrderItemId = split.OrderItemId;
                split.KitchenTasks.Add(task);
            }
            else
            {
                task.Quantity -= movedQuantity;
                split.KitchenTasks.Add(new KitchenTask
                {
                    KitchenTaskId = Guid.NewGuid(),
                    OrderItemId = split.OrderItemId,
                    KitchenStationId = task.KitchenStationId,
                    BatchNumber = task.BatchNumber,
                    Quantity = movedQuantity,
                    Status = task.Status,
                    Priority = task.Priority,
                    EstimatedMinutes = task.EstimatedMinutes,
                    QueuedAt = task.QueuedAt
                });
            }
            remaining -= movedQuantity;
        }
        if (item.Status != "PendingConfirmation" && remaining != 0)
        {
            throw new OrderingException(409, "Số lượng món và nhiệm vụ bếp không nhất quán.");
        }
        return split;
    }

    private static void UpdateStatus(Order order)
    {
        var active = order.OrderItems.Where(item => item.Quantity > item.CancelledQuantity && item.Status != "Cancelled").ToArray();
        order.Status = active.Length == 0 ? "Cancelled"
            : active.Any(item => item.Status == "PendingConfirmation") ? "PendingConfirmation"
            : active.All(item => item.Status == "Served") ? "Served"
            : active.All(item => item.Status is "Ready" or "Served") ? "Ready" : "Confirmed";
    }

    private static string Hash(TableTransferDTO request)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    }
}
