using Customer.DAL.Models;

namespace Customer.DAL.Interfaces;

public interface ILoyaltyRepository
{
    Task<List<LoyaltyTier>> GetAllTiersAsync(CancellationToken ct);

    Task<LoyaltyTier?> GetTierByIdAsync(Guid tierId, CancellationToken ct);

    Task<LoyaltyTier?> GetTierByCodeAsync(string code, CancellationToken ct);

    Task<LoyaltyTier?> GetDefaultTierAsync(CancellationToken ct);

    Task<LoyaltyTier?> GetTierForSpendAsync(decimal totalSpend, CancellationToken ct);

    Task<bool> HasAssignedCustomersAsync(Guid tierId, CancellationToken ct);

    Task<int> CountTiersAsync(CancellationToken ct);

    void CreateTier(LoyaltyTier tier);

    void DeleteTier(LoyaltyTier tier);

    Task<LoyaltyPolicy?> GetCurrentPolicyAsync(CancellationToken ct);

    void CreatePolicy(LoyaltyPolicy policy);

    Task<int> GetAvailablePointsAsync(Guid customerId, CancellationToken ct);

    Task<decimal> GetTotalSpentAsync(Guid customerId, CancellationToken ct);

    Task<PointReservation?> GetReservationByBillAsync(Guid billId, CancellationToken ct);

    Task<PointReservation?> GetReservationByIdAsync(Guid reservationId, CancellationToken ct);

    void CreateEntry(LoyaltyEntry entry);

    void CreateReservation(PointReservation reservation);
}

