using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface ICheckoutLockService
{
    Task Verify(Guid sessionId, string guestToken, CancellationToken ct);
    Task<CheckoutLockDTO> Begin(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<CheckoutLockDTO> Release(Guid sessionId, Guid id, CancellationToken ct);
    Task<CheckoutLockDTO> Bind(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct);
}
