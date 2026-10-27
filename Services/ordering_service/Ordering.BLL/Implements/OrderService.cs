using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed class OrderService(
    IOrderRepository orders,
    ICartRepository carts,
    IKitchenTaskRepository kitchen,
    IOrderingTransaction transaction,
    IOrderingAccess access,
    ICatalogOrderClient catalog,
    IOptions<OrderingSettings> settings,
    TimeProvider clock,
    ICheckoutLockRepository checkouts) : IOrderService
{
    public async Task<(OrderDTO Order, bool Created)> Create(
        Guid sessionId,
        string key,
        CreateOrderDTO request,
        string guestToken,
        CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, request.TableId, guestToken, ct);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl) || key != key.Trim())
        {
            throw new OrderingException(400, "Idempotency-Key is required, maximum 128 characters without surrounding whitespace.");
        }

        if (!actor.TableId.HasValue)
        {
            throw new OrderingException(400, "Choose a tableId when the session contains multiple tables.");
        }

        if (actor.IsGuest)
        {
            if (!request.CartId.HasValue || request.CartId == Guid.Empty || string.IsNullOrEmpty(request.CartVersion)
                || request.Items is null || request.Items.Count != 0)
            {
                throw new OrderingException(400, "Guests submit cartId and cartVersion, without an items payload.");
            }
        }
        else
        {
            if (request.CartId.HasValue || request.CartVersion is not null)
            {
                throw new OrderingException(400, "Staff orders use items, not a guest cart.");
            }

            OrderingRules.ValidateLines(request.Items, false);
        }

        var scopedKey = actor.Scope + OrderingRules.Hash(key);
        var requestHash = OrderingRules.Hash(new
        {
            sessionId,
            request.TableId,
            request.CartId,
            request.CartVersion,
            Items = request.Items.Select(line => new
            {
                line.ItemId,
                line.Quantity,
                Note = line.Note?.Trim(),
                Options = line.Options.OrderBy(option => option.OptionId).Select(option => new
                {
                    option.OptionId,
                    option.Quantity
                })
            })
        });

        var result = await transaction.ExecuteAsync(async () =>
        {
            await checkouts.LockSessionAsync(sessionId, ct);
            var existing = await orders.GetByKeyAsync(scopedKey, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new OrderingException(409, "Idempotency-Key was already used with a different request.");
                }

                return (Order: existing, Created: false);
            }

            if (await checkouts.GetActiveAsync(sessionId, ct) is not null)
            {
                throw new OrderingException(409, "Checkout has been requested for this session. Additional orders are blocked.");
            }

            Cart? cart = null;
            var lines = request.Items;
            if (actor.IsGuest)
            {
                cart = await carts.GetByIdAsync(request.CartId!.Value, ct);
                OrderingRules.CheckCart(cart, actor, clock.GetUtcNow().UtcDateTime, request.CartVersion);
                lines = OrderingRules.Selections(cart!);
            }

            OrderingRules.ValidateLines(lines, false);
            var quote = await catalog.ValidateAsync(actor, guestToken, lines, ct);
            if (!quote.IsValid)
            {
                throw new OrderingException(409, quote.Message);
            }

            var stations = new Dictionary<Guid, Guid>();
            if (!actor.IsGuest)
            {
                foreach (var snapshot in quote.Items)
                {
                    stations[snapshot.ItemId] = await Station(snapshot.KitchenStationId, ct);
                }
            }

            // Recheck the session immediately before persisting. No distributed lock is held in Restaurant.
            await access.RequireAsync(sessionId, actor.TableId, guestToken, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var orderId = Guid.NewGuid();
            var order = new Order
            {
                OrderId = orderId,
                OrderNumber = $"ORD-{orderId:N}",
                TableSessionRef = sessionId,
                Channel = !string.IsNullOrWhiteSpace(request.Channel) ? request.Channel : "DineIn",
                CreatedByRef = actor.UserId,
                IdempotencyKey = scopedKey,
                RequestHash = requestHash,
                CreatedAt = now,
                Status = actor.IsGuest ? "PendingConfirmation" : "Confirmed",
                ConfirmedAt = actor.IsGuest ? null : now,
                ConfirmedByRef = actor.UserId
            };

            for (var index = 0; index < quote.Items.Count; index++)
            {
                var snapshot = quote.Items[index];
                var item = new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    OrderId = orderId,
                    ItemRef = snapshot.ItemId,
                    RecipeVersionRef = snapshot.RecipeVersionId,
                    OriginalTableRef = actor.TableId,
                    NameSnapshot = snapshot.Name,
                    UnitPriceSnapshot = snapshot.UnitPrice,
                    Quantity = snapshot.Quantity,
                    Note = lines[index].Note?.Trim(),
                    Status = actor.IsGuest ? "PendingConfirmation" : "Pending",
                    OrderItemOptions = snapshot.Options.Select(option => new OrderItemOption
                    {
                        Id = Guid.NewGuid(),
                        OptionRef = option.OptionId,
                        NameSnapshot = option.Name,
                        ExtraPriceSnapshot = option.ExtraPrice,
                        Quantity = option.Quantity
                    }).ToList()
                };

                order.OrderItems.Add(item);
                if (!actor.IsGuest)
                {
                    var (initialStatus, priority) = DetermineCourseAndPriority(item.NameSnapshot, order.Channel);
                    item.Status = initialStatus;
                    var estimatedMinutes = snapshot.PreparationTimeMinutes.HasValue && snapshot.PreparationTimeMinutes.Value > 0
                        ? snapshot.PreparationTimeMinutes.Value
                        : (int?)null;
                    Enqueue(item, stations[item.ItemRef], actor.UserId!.Value, now, initialStatus, priority, estimatedMinutes);
                }
            }

            orders.Create(order);
            if (cart is not null)
            {
                carts.ReplaceItems(cart, []);
            }

            return (Order: order, Created: true);
        }, ct);

        return (Map(result.Order), result.Created);
    }

    public async Task<OrderDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, guestToken, ct);
        var order = await transaction.ExecuteAsync(async () =>
        {
            var found = await orders.GetByIdAsync(id, ct);
            CheckOrder(found, actor);
            return found!;
        }, ct);

        return Map(order);
    }

    public async Task<List<OrderDTO>> GetAll(Guid sessionId, int page, int pageSize, string guestToken, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, guestToken, ct);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new OrderingException(400, "Invalid pagination.");
        }

        var result = await transaction.ExecuteAsync(() => orders.GetAllAsync(
            sessionId,
            null,
            page,
            pageSize,
            ct), ct);

        return result.Select(Map).ToList();
    }

    public async Task<OrderDTO> Confirm(Guid sessionId, Guid id, string version, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, string.Empty, ct);
        if (actor.IsGuest || !actor.UserId.HasValue)
        {
            throw new OrderingException(403, "Staff permission is required to confirm orders.");
        }

        var expected = new byte[8];
        if (version is null || !Convert.TryFromBase64String(version, expected, out var size) || size != 8)
        {
            throw new OrderingException(400, "A valid order rowversion is required.");
        }

        var order = await transaction.ExecuteAsync(async () =>
        {
            await checkouts.LockSessionAsync(sessionId, ct);
            var checkout = await checkouts.GetActiveAsync(sessionId, ct);
            var found = await orders.GetByIdAsync(id, ct);
            CheckOrder(found, actor);
            if (found!.Status == "Confirmed")
            {
                return found;
            }

            if (checkout?.Status == "Frozen")
            {
                throw new OrderingException(409, "The checkout item set has already been frozen.");
            }

            if (found.Status != "PendingConfirmation" || !found.Version.SequenceEqual(expected))
            {
                throw new OrderingException(409, "The order is no longer pending or its version changed.");
            }

            var lines = found.OrderItems.OrderBy(item => item.OrderItemId).Select(item => new SelectionDTO
            {
                ItemId = item.ItemRef,
                Quantity = item.Quantity,
                Options = item.OrderItemOptions.Select(option => new OptionSelectionDTO
                {
                    OptionId = option.OptionRef,
                    Quantity = option.Quantity
                }).ToList()
            }).ToList();

            var quote = await catalog.ValidateAsync(actor, string.Empty, lines, ct);
            if (!quote.IsValid)
            {
                throw new OrderingException(409, quote.Message);
            }

            var stations = new Dictionary<Guid, Guid>();
            var prepTimes = new Dictionary<Guid, int?>();
            foreach (var snapshot in quote.Items)
            {
                stations[snapshot.ItemId] = await Station(snapshot.KitchenStationId, ct);
                prepTimes[snapshot.ItemId] = snapshot.PreparationTimeMinutes;
            }

            await access.RequireAsync(sessionId, null, string.Empty, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            orders.SetOriginalVersion(found, expected);
            found.Status = "Confirmed";
            found.ConfirmedAt = now;
            found.ConfirmedByRef = actor.UserId;
            foreach (var item in found.OrderItems)
            {
                var (initialStatus, priority) = DetermineCourseAndPriority(item.NameSnapshot, found.Channel);
                item.Status = initialStatus;
                var estimatedMinutes = prepTimes.TryGetValue(item.ItemRef, out var pt) && pt.HasValue && pt.Value > 0
                    ? pt.Value
                    : (int?)null;
                Enqueue(item, stations[item.ItemRef], actor.UserId.Value, now, initialStatus, priority, estimatedMinutes);
            }

            return found;
        }, ct);

        return Map(order);
    }

    public async Task<OrderDTO> ServeItem(Guid sessionId, Guid id, Guid orderItemId, string? version, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, string.Empty, ct);
        if (actor.IsGuest || !actor.UserId.HasValue)
        {
            throw new OrderingException(403, "Staff permission is required to serve order items.");
        }

        var order = await transaction.ExecuteAsync(async () =>
        {
            var found = await orders.GetByIdAsync(id, ct);
            CheckOrder(found, actor);

            if (!string.IsNullOrEmpty(version))
            {
                var expected = new byte[8];
                if (!Convert.TryFromBase64String(version, expected, out var size) || size != 8)
                {
                    throw new OrderingException(400, "A valid order rowversion is required.");
                }

                if (found!.Version is not null && !found.Version.SequenceEqual(expected))
                {
                    throw new OrderingException(409, "The order changed. Reload before serving.");
                }
            }

            var item = found!.OrderItems.FirstOrDefault(i => i.OrderItemId == orderItemId);
            if (item is null)
            {
                throw new OrderingException(404, "Order item not found.");
            }

            if (!string.Equals(item.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                throw new OrderingException(400, $"Cannot serve item in '{item.Status}' status. Must be 'Ready'.");
            }

            item.Status = "Served";
            var now = clock.GetUtcNow().UtcDateTime;

            foreach (var task in item.KitchenTasks)
            {
                if (string.Equals(task.Status, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = "Served";
                    task.Events.Add(new Event
                    {
                        EventId = Guid.NewGuid(),
                        KitchenTaskId = task.KitchenTaskId,
                        FromStatus = "Ready",
                        ToStatus = "Served",
                        ActorUserRef = actor.UserId,
                        OccurredAt = now
                    });
                }
            }

            var allServed = found.OrderItems.All(i => i.Status == "Served" || i.Status == "Cancelled");
            if (allServed)
            {
                found.Status = "Served";
            }

            return found;
        }, ct);

        return Map(order);
    }

    private async Task<Guid> Station(Guid? itemStation, CancellationToken ct)
    {
        var id = itemStation ?? settings.Value.DefaultKitchenStationId ?? await kitchen.GetOnlyActiveStationAsync(ct);
        if (!id.HasValue || !await kitchen.IsStationActiveAsync(id.Value, ct))
        {
            throw new OrderingException(409, itemStation.HasValue
                ? $"Assigned kitchen station {itemStation.Value} is missing or inactive. Ask Manager to update the item routing."
                : "No active kitchen station can be selected. Assign a station to the item or configure an active default station.");
        }

        return id.Value;
    }

    private void Enqueue(OrderItem item, Guid station, Guid actorId, DateTime now, string status, int priority, int? estimatedMinutes)
    {
        kitchen.Create(new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            KitchenStationId = station,
            OrderItem = item,
            OrderItemId = item.OrderItemId,
            BatchNumber = 1,
            Quantity = item.Quantity,
            Status = status,
            Priority = priority,
            EstimatedMinutes = estimatedMinutes,
            QueuedAt = now,
            Events = new List<Event>
            {
                new Event
                {
                    EventId = Guid.NewGuid(),
                    ToStatus = status,
                    ActorUserRef = actorId,
                    OccurredAt = now
                }
            }
        });
    }

    private static (string Status, int Priority) DetermineCourseAndPriority(string name, string? channel)
    {
        if (string.Equals(channel, "Takeaway", StringComparison.OrdinalIgnoreCase))
        {
            return ("Pending", 50);
        }

        var lower = name.ToLowerInvariant();
        if (lower.Contains("kem") || lower.Contains("chè") || lower.Contains("che") || lower.Contains("flan")
            || lower.Contains("tráng miệng") || lower.Contains("trang mieng") || lower.Contains("pudding")
            || lower.Contains("trái cây") || lower.Contains("trai cay") || lower.Contains("bánh ngọt"))
        {
            return ("Hold", 20);
        }

        if (lower.Contains("khai vị") || lower.Contains("khai vi") || lower.Contains("salad")
            || lower.Contains("gỏi") || lower.Contains("goi") || lower.Contains("súp") || lower.Contains("soup")
            || lower.Contains("khoai tây") || lower.Contains("chả giò") || lower.Contains("cha gio"))
        {
            return ("Pending", 100);
        }

        if (lower.Contains("trà") || lower.Contains("tra") || lower.Contains("cà phê") || lower.Contains("ca phe")
            || lower.Contains("nước") || lower.Contains("nuoc") || lower.Contains("sinh tố")
            || lower.Contains("soda") || lower.Contains("bia") || lower.Contains("juice"))
        {
            return ("Pending", 90);
        }

        if (lower.Contains("nướng") || lower.Contains("nuong") || lower.Contains("hầm") || lower.Contains("ham")
            || lower.Contains("tiềm") || lower.Contains("tiem") || lower.Contains("lẩu") || lower.Contains("lau")
            || lower.Contains("quay") || lower.Contains("ninh") || lower.Contains("nguyên con"))
        {
            return ("Pending", 70);
        }

        if (lower.Contains("xào") || lower.Contains("xao") || lower.Contains("chiên") || lower.Contains("chien")
            || lower.Contains("lúc lắc") || lower.Contains("áp chảo") || lower.Contains("mì") || lower.Contains("mi"))
        {
            return ("Pending", 50);
        }

        return ("Pending", 40);
    }

    private static void CheckOrder(Order? order, OrderingActor actor)
    {
        if (order is null || order.TableSessionRef != actor.SessionId)
        {
            throw new OrderingException(404, "Order not found.");
        }
    }

    private static OrderDTO Map(Order order)
    {
        var items = order.OrderItems.OrderBy(item => item.OrderItemId).Select(item => new OrderLineDTO
        {
            OrderItemId = item.OrderItemId,
            ItemId = item.ItemRef,
            RecipeVersionId = item.RecipeVersionRef,
            NameSnapshot = item.NameSnapshot,
            Quantity = item.Quantity,
            UnitPriceSnapshot = item.UnitPriceSnapshot,
            LineTotal = (item.UnitPriceSnapshot + item.OrderItemOptions.Sum(option => option.ExtraPriceSnapshot * option.Quantity)) * item.Quantity,
            Note = item.Note,
            Status = item.Status,
            Options = item.OrderItemOptions.OrderBy(option => option.OptionRef).Select(option => new QuotedOptionDTO
            {
                OptionId = option.OptionRef,
                Name = option.NameSnapshot,
                Quantity = option.Quantity,
                ExtraPrice = option.ExtraPriceSnapshot
            }).ToList()
        }).ToList();

        return new OrderDTO
        {
            OrderId = order.OrderId,
            OrderNumber = order.OrderNumber,
            TableSessionId = order.TableSessionRef!.Value,
            Channel = order.Channel,
            Source = order.CreatedByRef.HasValue ? "Staff" : "Guest",
            Status = order.Status,
            Version = Convert.ToBase64String(order.Version),
            CreatedBy = order.CreatedByRef,
            CreatedAt = OrderingRules.Utc(order.CreatedAt),
            ConfirmedBy = order.ConfirmedByRef,
            ConfirmedAt = order.ConfirmedAt.HasValue ? OrderingRules.Utc(order.ConfirmedAt.Value) : null,
            Items = items,
            TotalAmount = items.Sum(item => item.LineTotal)
        };
    }
}
