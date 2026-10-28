using System.Linq.Expressions;
using System.Globalization;
using Billing.Common.Constants;
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

    public async Task<IReadOnlyList<Payment>> GetByPayOsRequestKeyAsync(string key, CancellationToken ct)
    {
        return await Details().Where(value => value.IdempotencyKey == key || value.IdempotencyKey.StartsWith(key + ":"))
            .Take(2).ToListAsync(ct);
    }

    public Task<Payment?> GetByKeyAsync(string key, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(payment => payment.IdempotencyKey == key, ct);
    }

    public Task<Payment?> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        return Details().SingleOrDefaultAsync(payment => payment.PaymentId == id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetBillPaymentCountsAsync(Guid shiftId, CancellationToken ct, Expression<Func<Payment, bool>>? include = null)
    {
        var billIds = _context.Payments.Where(value => value.CashShiftId == shiftId).Select(value => value.BillId);
        return await _context.Payments.AsNoTracking().Where(include ?? (value => true)).Where(value => billIds.Contains(value.BillId))
            .GroupBy(value => value.BillId).Select(group => new
            {
                BillId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(value => value.BillId, value => value.Count, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetForReconciliationAsync(Guid shiftId, CancellationToken ct)
    {
        return await _context.Payments.AsNoTracking().Where(value => value.CashShiftId == shiftId)
            .Include(value => value.Bill).Include(value => value.CashMovements)
            .Include(value => value.CashHandover).Include(value => value.Refunds).ToListAsync(ct);
    }

    public void ResetProviderTracking()
    {
        if (_context.Database.CurrentTransaction is not null || _context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Provider stages require the preceding transaction to be saved and completed.");
        }

        _context.ChangeTracker.Clear();
    }

    public Task<bool> LockProviderWorkAsync(Guid id, CancellationToken ct)
    {
        return LockResource("Billing.PayOS.Work:" + id.ToString("N"), 1000, ct);
    }

    public Task<bool> LockBankReferenceAsync(string reference, CancellationToken ct)
    {
        return LockResource("Billing.PayOS.Reference:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference))), 10000, ct);
    }

    public Task<bool> BankReferenceUsedAsync(string reference, Guid exceptId, CancellationToken ct)
    {
        return _context.Payments.AsNoTracking().AnyAsync(value => value.PaymentId != exceptId && value.ProviderTransactionId == reference, ct);
    }

    public async Task<string> AllocateOrderCodeAsync(CancellationToken ct)
    {
        if (!await LockResource("Billing.PayOS.OrderCode", 10000, ct))
        {
            throw new InvalidOperationException("Provider order allocation is busy.");
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var number = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & ((1UL << 53) - 1);
            if (number < 1000000000000UL)
            {
                continue;
            }

            var text = number.ToString(CultureInfo.InvariantCulture);
            if (!await _context.Payments.AnyAsync(value => value.ProviderOrderCode == text, ct))
            {
                return text;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique provider order code.");
    }

    public Task<Payment?> GetForProviderUpdateAsync(Guid id, CancellationToken ct)
    {
        return _context.Payments.Include(value => value.Bill).ThenInclude(bill => bill.BillLines)
            .Include(value => value.Bill).ThenInclude(bill => bill.Payments).ThenInclude(value => value.CashMovements)
            .Include(value => value.Bill).ThenInclude(bill => bill.Payments).ThenInclude(value => value.Refunds)
            .Include(value => value.Bill).ThenInclude(bill => bill.Payments).ThenInclude(value => value.CashHandover)
            .Include(value => value.CashShift).ThenInclude(shift => shift.CashRegister)
            .AsSplitQuery().SingleOrDefaultAsync(value => value.PaymentId == id, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetByProviderCodeAsync(string code, CancellationToken ct)
    {
        return await Details().Where(value => value.ProviderOrderCode == code).Take(2).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Guid> Ids, int Total)> GetProviderBatchAsync(int skip, int take, CancellationToken ct)
    {
        var query = _context.Payments.AsNoTracking().Where(value => value.MethodCode == PayOsStates.Method
            && value.IdempotencyKey.StartsWith(PayOsStates.KeyPrefix) && value.ProviderOrderCode != null
            && value.Status != PaymentStatuses.Paid && value.Status != PayOsStates.ReviewRequired);
        var count = await query.CountAsync(ct);
        var ids = await query.OrderBy(value => value.PaymentId).Skip(skip).Take(take).Select(value => value.PaymentId).ToListAsync(ct);
        return (ids, count);
    }

    private async Task<bool> LockResource(string resource, int milliseconds, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Provider locks require an active transaction.");
        }

        if (!_context.Database.IsSqlServer())
        {
            return true;
        }

        var name = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = resource
        };
        var timeout = new SqlParameter("@timeout", SqlDbType.Int)
        {
            Value = milliseconds
        };
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        await _context.Database.ExecuteSqlRawAsync("EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=@timeout;",
            [result, name, timeout], ct);
        return (int)result.Value >= 0;
    }

    private IQueryable<Payment> Details()
    {
        return _context.Payments.AsNoTracking().Include(payment => payment.Bill)
            .Include(payment => payment.CashShift).ThenInclude(shift => shift.CashRegister)
            .Include(payment => payment.CashMovements).Include(payment => payment.Refunds).Include(payment => payment.CashHandover);
    }
}
