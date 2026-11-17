using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IOrderService
{
    Task<(OrderDTO Order, bool Created)> Create(Guid sessionId, string key, CreateOrderDTO request, string guestToken, CancellationToken ct);

    Task<OrderDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct);

    Task<List<OrderDTO>> GetAll(Guid sessionId, int page, int pageSize, string guestToken, CancellationToken ct);

    Task<OrderDTO> Confirm(Guid sessionId, Guid id, string version, CancellationToken ct);

    Task<(OrderDTO Order, bool Created)> CreateTakeaway(Guid restaurantId, string key, SubmitTakeawayDTO request, CancellationToken ct);

    Task<OrderDTO> GetTakeawayById(Guid restaurantId, Guid id, CancellationToken ct);

    Task<List<OrderDTO>> GetAllTakeaway(Guid restaurantId, string? status, int page, int pageSize, CancellationToken ct);

    Task<OrderDTO> CollectTakeaway(Guid restaurantId, Guid id, string version, string pickupCode, bool handoverConfirmed, CancellationToken ct);

    Task<OrderDTO> CancelTakeaway(Guid restaurantId, Guid id, string version, CancellationToken ct);

    Task<OrderDTO> AcceptTakeaway(Guid restaurantId, Guid id, string version, CancellationToken ct);

    Task<OrderDTO> RejectTakeaway(Guid restaurantId, Guid id, string version, string reason, CancellationToken ct);

    Task ReconcileTakeaway(CancellationToken ct);

    Task<RefundAccountDTO> GetRefundAccount(Guid restaurantId, Guid id, CancellationToken ct);
    Task<RefundAccountDTO> SaveRefundAccount(Guid restaurantId, Guid id, RefundAccountDTO request, CancellationToken ct);
    Task<OrderDTO> VerifyTakeawayRefund(Guid restaurantId, Guid id, string reference, string evidence, CancellationToken ct);

    Task<OrderDTO> ServeItem(Guid sessionId, Guid id, Guid orderItemId, string? version, CancellationToken ct);
}
