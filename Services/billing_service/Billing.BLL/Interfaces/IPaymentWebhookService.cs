namespace Billing.BLL.Interfaces;

public interface IPaymentWebhookService
{
    Task Process(string payload, CancellationToken ct);
}
