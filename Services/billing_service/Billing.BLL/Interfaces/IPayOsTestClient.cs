using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPayOsTestClient
{
    Task<PayOsProviderResult> CreateTestAsync(string clientId, PayOsCredentials credentials,
        string returnUrl, string cancelUrl, CancellationToken ct);
}
