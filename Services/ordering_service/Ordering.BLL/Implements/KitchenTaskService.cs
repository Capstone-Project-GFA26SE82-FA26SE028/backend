using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed class KitchenTaskService(
    IKitchenTaskRepository repository,
    IOrderingTransaction transaction,
    IKitchenStationAccess access,
    TimeProvider clock,
    ICheckoutLockRepository checkouts) : IKitchenTaskService
{
    public async Task<KitchenTaskPageDTO> GetAll(
        int page,
        int pageSize,
        Guid? stationId,
        string? status,
        Guid? orderId,
        Guid? sessionId,
        CancellationToken ct)
    {
        await access.RequireKitchenTaskAccessAsync(ct);

        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new OrderingException(400, "Invalid pagination.");
        }

        var (items, total) = await repository.GetPageAsync(
            page,
            pageSize,
            stationId,
            status,
            orderId,
            sessionId,
            ct);

        return new KitchenTaskPageDTO
        {
            Items = items.Select(Map).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<KitchenTaskDTO> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireKitchenTaskAccessAsync(ct);

        if (id == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid kitchen task ID.");
        }

        var task = await repository.GetByIdAsync(id, ct)
            ?? throw new OrderingException(404, "Kitchen task not found.");

        return Map(task);
    }

    public async Task<KitchenTaskDTO> SetStatus(Guid id, string status, string? version, CancellationToken ct)
    {
        var actorId = await access.RequireKitchenTaskAccessAsync(ct);

        if (id == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid kitchen task ID.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw new OrderingException(400, "Status is required.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var task = await repository.GetByIdForUpdateAsync(id, ct)
                ?? throw new OrderingException(404, "Kitchen task not found.");

            if (!string.IsNullOrEmpty(version))
            {
                var expected = Convert.FromBase64String(version);
                if (task.Version is not null && !task.Version.SequenceEqual(expected))
                {
                    throw new OrderingException(409, "The kitchen task changed. Reload before updating.");
                }
            }

            if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                && task.OrderItem?.Order?.TableSessionRef is Guid sessionId)
            {
                await checkouts.LockSessionAsync(sessionId, ct);
                if ((await checkouts.GetActiveAsync(sessionId, ct))?.Status == "Frozen")
                {
                    throw new OrderingException(409, "Checkout has frozen this item. Cancel the checkout before cancelling the item.");
                }
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var fromStatus = task.Status;

            if (string.Equals(status, "Cooking", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(task.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OrderingException(400, $"Cannot start cooking a task in '{task.Status}' status. Must be 'Pending'.");
                }

                task.Status = "Cooking";
                task.CookingAt = now;
                if (task.OrderItem is not null)
                {
                    task.OrderItem.Status = "Cooking";
                }
            }
            else if (string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(task.Status, "Cooking", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(task.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OrderingException(400, $"Cannot mark task as ready in '{task.Status}' status.");
                }

                task.Status = "Ready";
                task.ReadyAt = now;
                if (task.CookingAt is null)
                {
                    task.CookingAt = now;
                }

                if (task.OrderItem is not null)
                {
                    task.OrderItem.Status = "Ready";

                    if (task.OrderItem.Order is not null)
                    {
                        var allReady = task.OrderItem.Order.OrderItems.All(
                            i => i.Status == "Ready" || i.Status == "Served");

                        if (allReady)
                        {
                            task.OrderItem.Order.Status = "Ready";
                        }
                    }
                }
            }
            else if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(task.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OrderingException(400, $"Cannot cancel a kitchen task in '{task.Status}' status. Only 'Pending' tasks can be cancelled.");
                }

                task.Status = "Cancelled";
                if (task.OrderItem is not null)
                {
                    task.OrderItem.Status = "Cancelled";
                }
            }
            else
            {
                throw new OrderingException(400, $"Invalid target status '{status}'.");
            }

            task.Events.Add(new Event
            {
                EventId = Guid.NewGuid(),
                KitchenTaskId = task.KitchenTaskId,
                FromStatus = fromStatus,
                ToStatus = task.Status,
                ActorUserRef = actorId,
                OccurredAt = now
            });

            return Map(task);
        }, ct);
    }

    public async Task<KitchenTaskDTO> SetPriority(Guid id, int priority, string? version, CancellationToken ct)
    {
        await access.RequireKitchenTaskAccessAsync(ct);

        if (id == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid kitchen task ID.");
        }

        if (priority < 0)
        {
            throw new OrderingException(400, "Priority cannot be negative.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var task = await repository.GetByIdForUpdateAsync(id, ct)
                ?? throw new OrderingException(404, "Kitchen task not found.");

            if (!string.IsNullOrEmpty(version))
            {
                var expected = Convert.FromBase64String(version);
                if (task.Version is not null && !task.Version.SequenceEqual(expected))
                {
                    throw new OrderingException(409, "The kitchen task changed. Reload before updating.");
                }
            }

            task.Priority = priority;
            return Map(task);
        }, ct);
    }

    public async Task<KitchenSummaryDTO> GetSummary(Guid? stationId, CancellationToken ct)
    {
        await access.RequireKitchenTaskAccessAsync(ct);

        var (pending, cooking, ready, avgMinutes) = await repository.GetSummaryAsync(stationId, ct);

        return new KitchenSummaryDTO
        {
            PendingCount = pending,
            CookingCount = cooking,
            ReadyCount = ready,
            AvgCookingTimeMinutes = avgMinutes
        };
    }

    public async Task<List<KitchenBatchGroupDTO>> GetBatches(Guid? stationId, CancellationToken ct)
    {
        await access.RequireKitchenTaskAccessAsync(ct);
        var tasks = await repository.GetPendingForBatchesAsync(stationId, ct);

        var groups = tasks
            .GroupBy(t => new { t.OrderItem.ItemRef, t.KitchenStationId })
            .Select(g =>
            {
                var first = g.First();
                return new KitchenBatchGroupDTO
                {
                    ItemId = g.Key.ItemRef,
                    ItemName = first.OrderItem?.NameSnapshot ?? string.Empty,
                    KitchenStationId = g.Key.KitchenStationId,
                    KitchenStationName = first.KitchenStation?.KitchenStationName ?? string.Empty,
                    TotalQuantity = g.Sum(x => x.Quantity),
                    Tasks = g.Select(t => new KitchenBatchItemDTO
                    {
                        KitchenTaskId = t.KitchenTaskId,
                        OrderId = t.OrderItem.OrderId,
                        OrderNumber = t.OrderItem.Order?.OrderNumber ?? string.Empty,
                        TableCode = t.OrderItem.Order?.TableSessionRef.ToString() ?? string.Empty,
                        Quantity = t.Quantity,
                        Note = t.OrderItem.Note,
                        Priority = t.Priority,
                        EstimatedMinutes = t.EstimatedMinutes,
                        QueuedAt = t.QueuedAt
                    }).ToList()
                };
            })
            .OrderByDescending(b => b.TotalQuantity)
            .ToList();

        return groups;
    }

    public async Task<(int AffectedCount, string Status)> StartBatch(List<Guid> taskIds, CancellationToken ct)
    {
        var actorId = await access.RequireKitchenTaskAccessAsync(ct);
        if (taskIds is null || taskIds.Count == 0)
        {
            throw new OrderingException(400, "Provide at least one kitchen task ID to start.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var tasks = await repository.GetByIdsForUpdateAsync(taskIds, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            int count = 0;

            foreach (var task in tasks)
            {
                if (string.Equals(task.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = "Cooking";
                    task.CookingAt = now;
                    if (task.OrderItem is not null)
                    {
                        task.OrderItem.Status = "Cooking";
                    }

                    task.Events.Add(new Event
                    {
                        EventId = Guid.NewGuid(),
                        KitchenTaskId = task.KitchenTaskId,
                        FromStatus = "Pending",
                        ToStatus = "Cooking",
                        ActorUserRef = actorId,
                        OccurredAt = now
                    });
                    count++;
                }
            }

            return (count, "Cooking");
        }, ct);
    }

    public async Task<(int AffectedCount, string Status)> CompleteBatch(List<Guid> taskIds, CancellationToken ct)
    {
        var actorId = await access.RequireKitchenTaskAccessAsync(ct);
        if (taskIds is null || taskIds.Count == 0)
        {
            throw new OrderingException(400, "Provide at least one kitchen task ID to complete.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var tasks = await repository.GetByIdsForUpdateAsync(taskIds, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            int count = 0;

            foreach (var task in tasks)
            {
                if (string.Equals(task.Status, "Cooking", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(task.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    var fromStatus = task.Status;
                    task.Status = "Ready";
                    task.ReadyAt = now;
                    if (task.CookingAt is null)
                    {
                        task.CookingAt = now;
                    }

                    if (task.OrderItem is not null)
                    {
                        task.OrderItem.Status = "Ready";
                        if (task.OrderItem.Order is not null)
                        {
                            var allReady = task.OrderItem.Order.OrderItems.All(
                                i => i.Status == "Ready" || i.Status == "Served" || i.Status == "Cancelled");
                            if (allReady)
                            {
                                task.OrderItem.Order.Status = "Ready";
                            }
                        }
                    }

                    task.Events.Add(new Event
                    {
                        EventId = Guid.NewGuid(),
                        KitchenTaskId = task.KitchenTaskId,
                        FromStatus = fromStatus,
                        ToStatus = "Ready",
                        ActorUserRef = actorId,
                        OccurredAt = now
                    });
                    count++;
                }
            }

            return (count, "Ready");
        }, ct);
    }

    public async Task<KitchenTaskDTO> RemakeTask(Guid id, string reason, CancellationToken ct)
    {
        var actorId = await access.RequireKitchenTaskAccessAsync(ct);
        if (id == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid kitchen task ID.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var task = await repository.GetByIdForUpdateAsync(id, ct)
                ?? throw new OrderingException(404, "Kitchen task not found.");

            if (task.OrderItem?.Status == "Cancelled" && task.OrderItem.Order?.TableSessionRef is Guid sessionId)
            {
                await checkouts.LockSessionAsync(sessionId, ct);
                if ((await checkouts.GetActiveAsync(sessionId, ct))?.Status == "Frozen")
                {
                    throw new OrderingException(409, "A cancelled item cannot be restored during frozen checkout.");
                }
            }

            var fromStatus = task.Status;
            task.Status = "Cancelled";
            var now = clock.GetUtcNow().UtcDateTime;

            task.Events.Add(new Event
            {
                EventId = Guid.NewGuid(),
                KitchenTaskId = task.KitchenTaskId,
                FromStatus = fromStatus,
                ToStatus = "Cancelled",
                ActorUserRef = actorId,
                OccurredAt = now
            });

            var remakeReason = string.IsNullOrWhiteSpace(reason) ? "Làm lại do sự cố bếp" : reason.Trim();
            var newTask = new KitchenTask
            {
                KitchenTaskId = Guid.NewGuid(),
                KitchenStationId = task.KitchenStationId,
                OrderItemId = task.OrderItemId,
                BatchNumber = 1,
                Quantity = task.Quantity,
                Status = "Pending",
                Priority = 999,
                EstimatedMinutes = task.EstimatedMinutes,
                QueuedAt = now,
                Events = new List<Event>
                {
                    new Event
                    {
                        EventId = Guid.NewGuid(),
                        ToStatus = "Pending",
                        ActorUserRef = actorId,
                        OccurredAt = now
                    }
                }
            };

            if (task.OrderItem is not null)
            {
                task.OrderItem.Status = "Pending";
                task.OrderItem.Note = string.IsNullOrEmpty(task.OrderItem.Note)
                    ? $"[LÀM LẠI] {remakeReason}"
                    : $"{task.OrderItem.Note} [LÀM LẠI] {remakeReason}";
            }

            repository.Create(newTask);
            return Map(newTask);
        }, ct);
    }

    public async Task<FireCourseResultDTO> FireCourse(Guid sessionId, Guid? orderId, string course, CancellationToken ct)
    {
        var actorId = await access.RequireKitchenTaskAccessAsync(ct);
        if (sessionId == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid table session ID.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var holdTasks = await repository.GetHoldTasksAsync(sessionId, orderId, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            int count = 0;

            foreach (var task in holdTasks)
            {
                task.Status = "Pending";
                task.Priority = 85;
                task.QueuedAt = now;
                if (task.OrderItem is not null)
                {
                    task.OrderItem.Status = "Pending";
                }

                task.Events.Add(new Event
                {
                    EventId = Guid.NewGuid(),
                    KitchenTaskId = task.KitchenTaskId,
                    FromStatus = "Hold",
                    ToStatus = "Pending",
                    ActorUserRef = actorId,
                    OccurredAt = now
                });
                count++;
            }

            return new FireCourseResultDTO
            {
                ActivatedCount = count,
                Message = count > 0
                    ? $"Đã kích hoạt {count} món '{course ?? "Tráng miệng"}' cho bếp chế biến."
                    : "Không có món nào đang ở trạng thái giữ (Hold) cần kích hoạt."
            };
        }, ct);
    }

    private KitchenTaskDTO Map(KitchenTask task)
    {
        var effectiveStatus = string.Equals(task.OrderItem?.Status, "Served", StringComparison.OrdinalIgnoreCase)
            ? "Served"
            : task.Status;

        return new KitchenTaskDTO
        {
            KitchenTaskId = task.KitchenTaskId,
            KitchenStationId = task.KitchenStationId,
            KitchenStationCode = task.KitchenStation?.KitchenStationCode ?? string.Empty,
            KitchenStationName = task.KitchenStation?.KitchenStationName ?? string.Empty,
            OrderItemId = task.OrderItemId,
            OrderId = task.OrderItem?.OrderId ?? Guid.Empty,
            OrderNumber = task.OrderItem?.Order?.OrderNumber ?? string.Empty,
            TableSessionId = task.OrderItem?.Order?.TableSessionRef ?? Guid.Empty,
            OriginalTableId = task.OrderItem?.OriginalTableRef,
            ItemId = task.OrderItem?.ItemRef ?? Guid.Empty,
            NameSnapshot = task.OrderItem?.NameSnapshot ?? string.Empty,
            Quantity = task.Quantity,
            Note = task.OrderItem?.Note,
            Status = effectiveStatus,
            Priority = task.Priority,
            QueuedAt = task.QueuedAt,
            CookingAt = task.CookingAt,
            ReadyAt = task.ReadyAt,
            EstimatedMinutes = task.EstimatedMinutes,
            DueAt = task.CookingAt.HasValue && task.EstimatedMinutes.HasValue && task.EstimatedMinutes.Value > 0
                ? task.CookingAt.Value.AddMinutes(task.EstimatedMinutes.Value)
                : null,
            IsLate = task.CookingAt.HasValue && !task.ReadyAt.HasValue
                && task.EstimatedMinutes.HasValue && task.EstimatedMinutes.Value > 0
                && task.CookingAt.Value.AddMinutes(task.EstimatedMinutes.Value) < clock.GetUtcNow().UtcDateTime,
            Version = task.Version is not null ? Convert.ToBase64String(task.Version) : string.Empty,
            Options = task.OrderItem?.OrderItemOptions?.Select(o => new KitchenTaskOptionDTO
            {
                OptionId = o.OptionRef,
                Name = o.NameSnapshot,
                Quantity = o.Quantity
            }).ToList() ?? []
        };
    }
}

