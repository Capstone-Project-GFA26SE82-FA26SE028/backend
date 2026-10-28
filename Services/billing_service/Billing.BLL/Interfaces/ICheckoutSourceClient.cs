using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface ICheckoutSourceClient
{
    Task VerifyAsync(Guid sessionId, string guestToken, CancellationToken ct);
    Task<BillCheckoutDTO> BeginAsync(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task ReleaseAsync(Guid sessionId, Guid id, CancellationToken ct);
    Task BindAsync(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct);
}
