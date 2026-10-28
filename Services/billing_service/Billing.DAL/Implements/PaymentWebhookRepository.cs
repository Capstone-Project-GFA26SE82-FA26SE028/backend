using System.Data;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class PaymentWebhookRepository(BillingDbContext db) : GenericRepository<PaymentWebhook, BillingDbContext>(db), IPaymentWebhookRepository
{
    public async Task<bool> LockKeyAsync(string key, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Webhook deduplication requires a transaction.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = "Billing.PayOS.Webhook:" + key
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync("EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;",
            [result, resource], ct);
        return (int)result.Value >= 0;
    }

    public async Task<IReadOnlyList<PaymentWebhook>> FindAsync(string key, CancellationToken ct)
    {
        return await _context.PaymentWebhooks.Where(value => value.Provider == "PayOS" && value.DedupeKey == key).Take(2).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentWebhook>> PendingAsync(Guid paymentId, CancellationToken ct)
    {
        return await _context.PaymentWebhooks.Where(value => value.Provider == "PayOS" && value.PaymentId == paymentId && value.ProcessedAt == null).ToListAsync(ct);
    }
}
