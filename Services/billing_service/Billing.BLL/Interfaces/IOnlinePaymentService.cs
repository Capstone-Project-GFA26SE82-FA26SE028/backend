using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IOnlinePaymentService
{
    Task<OnlinePaymentDTO> Execute(OnlinePaymentCommand command, CancellationToken ct);
    Task<bool> ProcessWebhook(string payload, CancellationToken ct);
}
