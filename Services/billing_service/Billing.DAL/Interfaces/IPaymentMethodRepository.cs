using Billing.DAL.Models;

namespace Billing.DAL.Interfaces;

public interface IPaymentMethodRepository
{
    Task<PaymentMethod?> LockByCodeAsync(string code, CancellationToken ct);
}
