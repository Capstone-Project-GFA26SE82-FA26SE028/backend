using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed class OrderService(IOrderRepository orders, ICartRepository carts, IKitchenTaskRepository kitchen,
    IOrderingTransaction transaction, IOrderingAccess access, ICatalogOrderClient catalog,
    IOptions<OrderingSettings> settings, TimeProvider clock) : IOrderService
{
    public async Task<(OrderDTO Order, bool Created)> Create(Guid sessionId, string key, CreateOrderDTO request,
        string guestToken, CancellationToken ct)
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
            var existing = await orders.GetByKeyAsync(scopedKey, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new OrderingException(409, "Idempotency-Key was already used with a different request.");
                }

                return (Order: existing, Created: false);
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
                Channel = "DineIn",
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
                    Enqueue(item, stations[item.ItemRef], actor.UserId!.Value, now);
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

        var result = await transaction.ExecuteAsync(() => orders.GetAllAsync(sessionId,
            actor.IsGuest ? actor.Scope : null, page, pageSize, ct), ct);
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
            var found = await orders.GetByIdAsync(id, ct);
            CheckOrder(found, actor);
            if (found!.Status == "Confirmed")
            {
                return found;
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
            foreach (var snapshot in quote.Items)
            {
                stations[snapshot.ItemId] = await Station(snapshot.KitchenStationId, ct);
            }
            await access.RequireAsync(sessionId, null, string.Empty, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            orders.SetOriginalVersion(found, expected);
            found.Status = "Confirmed";
            found.ConfirmedAt = now;
            found.ConfirmedByRef = actor.UserId;
            foreach (var item in found.OrderItems)
            {
                item.Status = "Pending";
                Enqueue(item, stations[item.ItemRef], actor.UserId.Value, now);
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
            throw new OrderingException(503, "Configure an active Ordering:DefaultKitchenStationId before sending orders to the kitchen.");
        }

        return id.Value;
    }

    private void Enqueue(OrderItem item, Guid station, Guid actorId, DateTime now)
    {
        kitchen.Create(new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            KitchenStationId = station,
            OrderItem = item,
            OrderItemId = item.OrderItemId,
            BatchNumber = 1,
            Quantity = item.Quantity,
            Status = "Pending",
            Priority = 0,
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
        });
    }

    private static void CheckOrder(Order? order, OrderingActor actor)
    {
        if (order is null || order.TableSessionRef != actor.SessionId
            || (actor.IsGuest && !order.IdempotencyKey.StartsWith(actor.Scope, StringComparison.Ordinal)))
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
