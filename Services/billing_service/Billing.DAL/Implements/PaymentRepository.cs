using System.Data;
using System.Security.Cryptography;
using System.Text;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Repositories;

namespace Billing.DAL.Implements;

public sealed class PaymentRepository(BillingDbContext db)
    : GenericRepository<Payment, BillingDbContext>(db), IPaymentRepository
{
    public async Task<bool> LockIdempotencyKeyAsync(string key, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to lock a payment request.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = "Billing.Payment.Key:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;",
            [result, resource], ct);
        return (int)result.Value >= 0;
    }

    public Task<Payment?> GetByKeyAsync(string key, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(payment => payment.IdempotencyKey == key, ct);
    }

    public Task<Payment?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(payment => payment.PaymentId == id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetBillPaymentCountsAsync(Guid shiftId, CancellationToken ct)
    {
        var billIds = _context.Payments.Where(value => value.CashShiftId == shiftId).Select(value => value.BillId);
        return await _context.Payments.AsNoTracking().Where(value => billIds.Contains(value.BillId))
            .GroupBy(value => value.BillId).Select(group => new { BillId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.BillId, value => value.Count, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetForReconciliationAsync(Guid shiftId, CancellationToken ct)
    {
        return await _context.Payments.AsNoTracking().Where(value => value.CashShiftId == shiftId)
            .Include(value => value.Bill).Include(value => value.CashMovements)
            .Include(value => value.CashHandover).Include(value => value.Refunds).ToListAsync(ct);
    }

    private IQueryable<Payment> Details()
    {
        return _context.Payments.AsNoTracking().Include(payment => payment.Bill)
            .Include(payment => payment.CashShift).ThenInclude(shift => shift.CashRegister)
            .Include(payment => payment.CashMovements);
    }
}
