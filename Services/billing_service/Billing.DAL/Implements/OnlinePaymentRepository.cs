using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Implements;

public sealed class OnlinePaymentRepository(BillingDbContext db) : IOnlinePaymentRepository
{
    public Task<OnlinePayment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.OnlinePayments.FromSqlInterpolated($"SELECT * FROM [OnlinePayments] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderId] = {id}")
            : db.OnlinePayments.Where(value => value.OrderId == id);
        return query.SingleOrDefaultAsync(ct);
    }

    public Task<OnlinePayment?> GetByCodeAsync(long code, CancellationToken ct)
    {
        return db.OnlinePayments.SingleOrDefaultAsync(value => value.ProviderOrderCode == code, ct);
    }

    public Task<List<Guid>> GetPendingAsync(CancellationToken ct)
    {
        return db.OnlinePayments.Where(value => value.Status == "Pending")
            .OrderBy(value => value.ExpiresAt).Select(value => value.OrderId).Take(100).ToListAsync(ct);
    }

    public void Create(OnlinePayment payment)
    {
        db.OnlinePayments.Add(payment);
    }
}
