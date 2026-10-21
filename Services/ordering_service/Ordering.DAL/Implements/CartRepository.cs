using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class CartRepository(OrderingDbContext db) : ICartRepository
{
    public Task<Cart?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.Carts.FromSqlInterpolated($"SELECT * FROM [Cart] WITH (UPDLOCK, HOLDLOCK) WHERE [CartId] = {id}")
            : db.Carts.Where(cart => cart.CartId == id);
        return query.Include(cart => cart.CartItems).ThenInclude(item => item.CartItemOptions).AsSplitQuery().SingleOrDefaultAsync(ct);
    }

    public async Task<Cart?> GetForGuestAsync(Guid sessionId, Guid guestId, DateTime now, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.Carts.FromSqlInterpolated($"SELECT * FROM [Cart] WITH (UPDLOCK, HOLDLOCK) WHERE [TableSessionRef] = {sessionId} AND [GuestAccessRef] = {guestId} AND [ExpiredAt] > {now}")
            : db.Carts.Where(cart => cart.TableSessionRef == sessionId && cart.GuestAccessRef == guestId && cart.ExpiredAt > now);
        return await query.Include(cart => cart.CartItems).ThenInclude(item => item.CartItemOptions)
            .AsSplitQuery().OrderBy(cart => cart.CartId).FirstOrDefaultAsync(ct);
    }

    public void Create(Cart cart)
    {
        db.Carts.Add(cart);
    }

    public void ReplaceItems(Cart cart, ICollection<CartItem> items)
    {
        db.CartItemOptions.RemoveRange(cart.CartItems.SelectMany(item => item.CartItemOptions));
        db.CartItems.RemoveRange(cart.CartItems);
        cart.CartItems = items;
    }

    public void Delete(Cart cart)
    {
        ReplaceItems(cart, []);
        db.Carts.Remove(cart);
    }
}
