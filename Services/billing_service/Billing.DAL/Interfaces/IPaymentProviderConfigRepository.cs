using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IPaymentProviderConfigRepository
{
    Task<PaymentProviderConfig?> GetByRestaurantAsync(Guid restaurantId, CancellationToken ct);
    PaymentProviderConfig? GetForRuntime(Guid restaurantId);
    void Create(PaymentProviderConfig configuration);
}
