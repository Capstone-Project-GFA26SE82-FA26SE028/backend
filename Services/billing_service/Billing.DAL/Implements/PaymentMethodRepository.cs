using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class PaymentMethodRepository(BillingDbContext db) : IPaymentMethodRepository
{
    public Task<PaymentMethod?> LockByCodeAsync(string code, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock a payment method.");
        }

        var source = db.Database.IsSqlServer()
            ? db.PaymentMethods.FromSqlInterpolated($"SELECT * FROM dbo.PaymentMethod WITH (UPDLOCK, HOLDLOCK) WHERE MethodCode={code}")
            : db.PaymentMethods.Where(method => method.MethodCode == code);
        return source.SingleOrDefaultAsync(ct);
    }
}
