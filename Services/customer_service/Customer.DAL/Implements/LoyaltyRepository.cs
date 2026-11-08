using Customer.DAL.Interfaces;
using Customer.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Customer.DAL.Implements;

public sealed class LoyaltyRepository(CustomerDbContext db) : ILoyaltyRepository
{
    public Task<List<LoyaltyTier>> GetAllTiersAsync(CancellationToken ct)
    {
        return db.LoyaltyTiers
            .AsNoTracking()
            .OrderBy(t => (double)t.RequiredSpend)
            .ToListAsync(ct);
    }

    public Task<LoyaltyTier?> GetTierByIdAsync(Guid tierId, CancellationToken ct)
    {
        return db.LoyaltyTiers.FirstOrDefaultAsync(t => t.LoyaltyTierId == tierId, ct);
    }

    public Task<LoyaltyTier?> GetDefaultTierAsync(CancellationToken ct)
    {
        return db.LoyaltyTiers
            .OrderBy(t => (double)t.RequiredSpend)
            .FirstOrDefaultAsync(ct);
    }

    public Task<LoyaltyTier?> GetTierForSpendAsync(decimal totalSpend, CancellationToken ct)
    {
        return db.LoyaltyTiers
            .Where(t => t.RequiredSpend <= totalSpend)
            .OrderByDescending(t => (double)t.RequiredSpend)
            .FirstOrDefaultAsync(ct);
    }

    public Task<LoyaltyPolicy?> GetCurrentPolicyAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.LoyaltyPolicies
            .Where(p => p.EffectiveFrom <= now)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetAvailablePointsAsync(Guid customerId, CancellationToken ct)
    {
        var netPoints = await db.LoyaltyEntries
            .Where(e => e.CustomerId == customerId)
            .SumAsync(e => (int?)e.SignedPoint, ct) ?? 0;

        var reserved = await db.PointReservations
            .Where(r => r.CustomerId == customerId && r.Status == "Pending" && r.ExpiredAt > DateTime.UtcNow)
            .SumAsync(r => (int?)r.Point, ct) ?? 0;

        var available = netPoints - reserved;
        return available > 0 ? available : 0;
    }

    public async Task<decimal> GetTotalSpentAsync(Guid customerId, CancellationToken ct)
    {
        var spends = await db.LoyaltyEntries
            .Where(e => e.CustomerId == customerId && e.Type == "Earn")
            .Select(e => e.EligibleSpend)
            .ToListAsync(ct);

        return spends.Sum();
    }

    public Task<PointReservation?> GetReservationByBillAsync(Guid billId, CancellationToken ct)
    {
        return db.PointReservations
            .FirstOrDefaultAsync(r => r.BillRef == billId && r.Status == "Pending", ct);
    }

    public Task<PointReservation?> GetReservationByIdAsync(Guid reservationId, CancellationToken ct)
    {
        return db.PointReservations
            .FirstOrDefaultAsync(r => r.PointReservationId == reservationId, ct);
    }

    public void CreateEntry(LoyaltyEntry entry)
    {
        db.LoyaltyEntries.Add(entry);
    }

    public void CreateReservation(PointReservation reservation)
    {
        db.PointReservations.Add(reservation);
    }
}

