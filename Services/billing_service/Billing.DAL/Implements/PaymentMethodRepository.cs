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
            throw new InvalidOperationException("Cần có giao dịch đang hoạt động để khóa phương thức thanh toán.");
        }

        var source = db.Database.IsSqlServer()
            ? db.PaymentMethods.FromSqlInterpolated($"SELECT * FROM dbo.PaymentMethod WITH (UPDLOCK, HOLDLOCK) WHERE MethodCode={code}")
            : db.PaymentMethods.Where(method => method.MethodCode == code);
        return source.SingleOrDefaultAsync(ct);
    }
}
