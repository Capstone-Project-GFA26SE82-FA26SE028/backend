using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IBillSourceClient
{
    Task<BillSourceDTO> GetBySessionAsync(Guid sessionId, CancellationToken ct);
}
