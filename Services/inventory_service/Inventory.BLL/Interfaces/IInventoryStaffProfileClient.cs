namespace Inventory.BLL.Interfaces;

public interface IInventoryStaffProfileClient
{
    Task<string?> GetFullNameAsync(Guid restaurantId, Guid userId, CancellationToken ct);
}
