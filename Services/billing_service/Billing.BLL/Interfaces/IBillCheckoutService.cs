using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IBillCheckoutService
{
    Task<BillCheckoutDTO> Request(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<BillCheckoutDTO> GetCurrent(Guid sessionId, string guestToken, CancellationToken ct);
    Task<BillCheckoutDTO> Synchronize(Guid sessionId, Guid id, CancellationToken ct);
    Task<BillCheckoutDTO> Cancel(Guid sessionId, Guid id, CancellationToken ct);
    Task AuthorizeRelease(Guid sessionId, Guid id, CancellationToken ct);
}
