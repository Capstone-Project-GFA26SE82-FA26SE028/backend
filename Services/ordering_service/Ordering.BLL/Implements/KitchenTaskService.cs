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
    TimeProvider clock) : IKitchenTaskService
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

    private static KitchenTaskDTO Map(KitchenTask task)
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

