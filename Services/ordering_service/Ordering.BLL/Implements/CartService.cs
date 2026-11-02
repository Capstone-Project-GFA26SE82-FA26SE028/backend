using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed class CartService(ICartRepository carts, IOrderingTransaction transaction,
    IOrderingAccess access, ICatalogOrderClient catalog, TimeProvider clock) : ICartService
{
    public async Task<CartDTO> Create(Guid sessionId, string guestToken, CancellationToken ct)
    {
        var actor = await Guest(sessionId, guestToken, ct);
        var cart = await transaction.ExecuteAsync(async () =>
        {
            var existing = await carts.GetForGuestAsync(sessionId, actor.GuestId!.Value, clock.GetUtcNow().UtcDateTime, ct);
            if (existing is not null)
            {
                return existing;
            }

            var created = new Cart
            {
                CartId = Guid.NewGuid(),
                TableSessionRef = sessionId,
                GuestAccessRef = actor.GuestId,
                ExpiredAt = actor.ExpiresAt!.Value.UtcDateTime
            };
            carts.Create(created);
            return created;
        }, ct);
        return await Map(cart, actor, guestToken, ct);
    }

    public async Task<CartDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        var actor = await Guest(sessionId, guestToken, ct);
        var cart = await transaction.ExecuteAsync(async () =>
        {
            var found = await carts.GetByIdAsync(id, ct);
            OrderingRules.CheckCart(found, actor, clock.GetUtcNow().UtcDateTime);
            return found!;
        }, ct);
        return await Map(cart, actor, guestToken, ct);
    }

    public async Task<CartDTO> Update(Guid sessionId, Guid id, CartWriteDTO request, string guestToken, CancellationToken ct)
    {
        var actor = await Guest(sessionId, guestToken, ct);
        OrderingRules.ValidateLines(request.Items, true);
        if (string.IsNullOrEmpty(request.Version))
        {
            throw new OrderingException(400, "Yêu cầu version của giỏ hàng.");
        }

        var result = await transaction.ExecuteAsync(async () =>
        {
            var found = await carts.GetByIdAsync(id, ct);
            OrderingRules.CheckCart(found, actor, clock.GetUtcNow().UtcDateTime, request.Version);
            var quote = await Quote(actor, guestToken, request.Items, ct);
            if (!quote.IsValid)
            {
                throw new OrderingException(409, quote.Message);
            }

            carts.ReplaceItems(found!, request.Items.Select(line => new CartItem
            {
                CartItemId = Guid.NewGuid(),
                CartId = id,
                ItemRef = line.ItemId,
                Quantity = line.Quantity,
                Note = line.Note?.Trim(),
                CartItemOptions = line.Options.Select(option => new CartItemOption
                {
                    CartItemOptionId = Guid.NewGuid(),
                    OptionRef = option.OptionId,
                    Quantity = option.Quantity
                }).ToList()
            }).ToList());
            return (Cart: found!, Quote: quote);
        }, ct);
        return new CartDTO
        {
            CartId = result.Cart.CartId,
            TableSessionId = result.Cart.TableSessionRef,
            ExpiresAt = OrderingRules.Utc(result.Cart.ExpiredAt),
            Version = OrderingRules.CartVersion(result.Cart),
            Items = request.Items,
            Quote = result.Quote
        };
    }

    public async Task Delete(Guid sessionId, Guid id, string version, string guestToken, CancellationToken ct)
    {
        var actor = await Guest(sessionId, guestToken, ct);
        if (string.IsNullOrEmpty(version))
        {
            throw new OrderingException(400, "Yêu cầu version của giỏ hàng.");
        }

        await transaction.ExecuteAsync(async () =>
        {
            var cart = await carts.GetByIdAsync(id, ct);
            OrderingRules.CheckCart(cart, actor, clock.GetUtcNow().UtcDateTime, version);
            carts.Delete(cart!);
            return true;
        }, ct);
    }

    private async Task<OrderingActor> Guest(Guid sessionId, string token, CancellationToken ct)
    {
        var actor = await access.RequireAsync(sessionId, null, token, ct);
        if (!actor.IsGuest)
        {
            throw new OrderingException(403, "Giỏ hàng dành cho khách. Nhân viên có thể tạo đơn hàng trực tiếp.");
        }

        return actor;
    }

    private async Task<CartDTO> Map(Cart cart, OrderingActor actor, string token, CancellationToken ct)
    {
        var lines = OrderingRules.Selections(cart);
        return new CartDTO
        {
            CartId = cart.CartId,
            TableSessionId = cart.TableSessionRef,
            ExpiresAt = OrderingRules.Utc(cart.ExpiredAt),
            Version = OrderingRules.CartVersion(cart),
            Items = lines,
            Quote = await Quote(actor, token, lines, ct)
        };
    }

    private Task<QuoteDTO> Quote(OrderingActor actor, string token, List<SelectionDTO> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            return Task.FromResult(new QuoteDTO
            {
                IsValid = true,
                TotalAmount = 0,
                EvaluatedAt = clock.GetUtcNow()
            });
        }

        return catalog.ValidateAsync(actor, token, lines, ct);
    }
}
