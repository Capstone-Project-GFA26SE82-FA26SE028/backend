using Microsoft.EntityFrameworkCore;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Implements;

public sealed class StaffTransferReceiptRepository(RestaurantDbContext db) : IStaffTransferReceiptRepository
{
    public Task<StaffTransferReceipt?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Set<StaffTransferReceipt>().SingleOrDefaultAsync(item => item.StaffTransferReceiptId == id, ct);
    }

    public void Create(StaffTransferReceipt receipt)
    {
        db.Set<StaffTransferReceipt>().Add(receipt);
    }
}
