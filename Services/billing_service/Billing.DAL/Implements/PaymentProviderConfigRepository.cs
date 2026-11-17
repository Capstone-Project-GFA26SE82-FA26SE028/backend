using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class PaymentProviderConfigRepository(BillingDbContext db) : IPaymentProviderConfigRepository
{
    public Task<PaymentProviderConfig?> GetByRestaurantAsync(Guid restaurantId, CancellationToken ct)
    {
        return db.PaymentProviderConfigs.SingleOrDefaultAsync(value => value.RestaurantId == restaurantId && value.Provider == "PayOS", ct);
    }

    public PaymentProviderConfig? GetForRuntime(Guid restaurantId)
    {
        return db.PaymentProviderConfigs.AsNoTracking().SingleOrDefault(value => value.RestaurantId == restaurantId && value.Provider == "PayOS");
    }

    public void Create(PaymentProviderConfig configuration)
    {
        db.PaymentProviderConfigs.Add(configuration);
    }
}
