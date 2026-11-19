using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface IStaffTransferReceiptRepository
{
    Task<StaffTransferReceipt?> GetByIdAsync(Guid id, CancellationToken ct);
    void Create(StaffTransferReceipt receipt);
}
