using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IBillingAccess
{
    Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken cancellationToken);
}
