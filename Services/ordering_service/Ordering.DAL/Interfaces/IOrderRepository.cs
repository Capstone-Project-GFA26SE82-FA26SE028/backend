using Ordering.DAL.Models;

namespace Ordering.DAL.Interfaces;

public interface IOrderRepository
{
    Task<List<Order>> GetForBillingAsync(Guid sessionId, CancellationToken ct);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Order?> GetByKeyAsync(string key, CancellationToken ct);

    Task<List<Order>> GetAllAsync(Guid sessionId, string? guestScope, int page, int pageSize, CancellationToken ct);

    void Create(Order order);

    void SetOriginalVersion(Order order, byte[] version);
}
