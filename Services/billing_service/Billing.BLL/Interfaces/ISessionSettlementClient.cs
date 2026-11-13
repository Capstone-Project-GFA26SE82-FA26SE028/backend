using Shared.Common.Billing;

namespace Billing.BLL.Interfaces;

public interface ISessionSettlementClient
{
    Task<bool> TryCloseAsync(PaidSessionSettlement payment, CancellationToken ct);
}
