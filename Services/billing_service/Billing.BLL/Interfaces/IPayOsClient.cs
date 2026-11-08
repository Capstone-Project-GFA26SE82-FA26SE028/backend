using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPayOsClient
{
    bool IsConfigured { get; }
    void EnsureConfigured();
    void EnsureCanCreate();
    Task<PayOsProviderResult> CreateAsync(PayOsCreateCommand request, CancellationToken ct);
    Task<PayOsProviderResult> GetAsync(long orderCode, CancellationToken ct);
    Task<PayOsProviderResult> CancelAsync(long orderCode, string reason, CancellationToken ct);
    PayOsWebhookDTO VerifyWebhook(string payload);
}
