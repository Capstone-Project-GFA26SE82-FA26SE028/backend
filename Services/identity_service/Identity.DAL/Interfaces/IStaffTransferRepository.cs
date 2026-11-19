using Identity.Domain.Models;

namespace Identity.DAL.Interfaces;

public interface IStaffTransferRepository
{
    Task<StaffTransfer?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Guid>> GetPendingAsync(CancellationToken ct);
    Task<bool> HasPendingAsync(Guid userId, CancellationToken ct);
    void Create(StaffTransfer transfer);
}
