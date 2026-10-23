using Customer.DAL.Models;

namespace Customer.DAL.Interfaces;

public interface ILoyaltyRepository
{
    Task<List<LoyaltyTier>> GetAllTiersAsync(CancellationToken ct);

    Task<LoyaltyTier?> GetTierByIdAsync(Guid tierId, CancellationToken ct);

    Task<LoyaltyTier?> GetDefaultTierAsync(CancellationToken ct);

    Task<LoyaltyTier?> GetTierForSpendAsync(decimal totalSpend, CancellationToken ct);

    Task<LoyaltyPolicy?> GetCurrentPolicyAsync(CancellationToken ct);

    Task<int> GetAvailablePointsAsync(Guid customerId, CancellationToken ct);

    Task<decimal> GetTotalSpentAsync(Guid customerId, CancellationToken ct);

    Task<PointReservation?> GetReservationByBillAsync(Guid billId, CancellationToken ct);

    Task<PointReservation?> GetReservationByIdAsync(Guid reservationId, CancellationToken ct);

    void CreateEntry(LoyaltyEntry entry);

    void CreateReservation(PointReservation reservation);
}

