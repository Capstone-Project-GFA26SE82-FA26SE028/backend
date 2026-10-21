using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IOrderService
{
    Task<(OrderDTO Order, bool Created)> Create(Guid sessionId, string key, CreateOrderDTO request, string guestToken, CancellationToken ct);

    Task<OrderDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct);

    Task<List<OrderDTO>> GetAll(Guid sessionId, int page, int pageSize, string guestToken, CancellationToken ct);

    Task<OrderDTO> Confirm(Guid sessionId, Guid id, string version, CancellationToken ct);
}
