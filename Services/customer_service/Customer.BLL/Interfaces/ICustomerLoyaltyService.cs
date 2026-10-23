using Customer.Common.DTOs;

namespace Customer.BLL.Interfaces;

public interface ICustomerLoyaltyService
{
    Task<CustomerDTO?> GetById(Guid id, CancellationToken ct);

    Task<CustomerDTO?> GetByPhone(string phone, CancellationToken ct);

    Task<CustomerDTO> GetOrCreate(CreateCustomerDTO request, CancellationToken ct);

    Task<(List<CustomerDTO> Items, int Total)> GetAll(int page, int pageSize, string? keyword, CancellationToken ct);

    Task<List<LoyaltyTierDTO>> GetTiers(CancellationToken ct);

    Task<LoyaltyEarnResultDTO> EarnPoints(LoyaltyEarnRequestDTO request, CancellationToken ct);

    Task<PointReservationResultDTO> ReservePoints(PointReservationRequestDTO request, CancellationToken ct);

    Task CommitPoints(Guid billId, CancellationToken ct);

    Task CancelPoints(Guid billId, CancellationToken ct);
}

