using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface ICartRepository
{
    Task<Cart?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Cart?> GetForGuestAsync(Guid sessionId, Guid guestId, DateTime now, CancellationToken ct);

    void Create(Cart cart);

    void ReplaceItems(Cart cart, ICollection<CartItem> items);

    void Delete(Cart cart);
}
