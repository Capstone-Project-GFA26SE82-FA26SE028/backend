using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface ICartRepository
{
    Task<Cart?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Cart?> GetForSessionAsync(Guid sessionId, CancellationToken ct);

    Task<Cart?> GetForCustomerAsync(Guid restaurantId, Guid customerId, DateTime now, CancellationToken ct);

    void Create(Cart cart);

    void ReplaceItems(Cart cart, ICollection<CartItem> items);

}
