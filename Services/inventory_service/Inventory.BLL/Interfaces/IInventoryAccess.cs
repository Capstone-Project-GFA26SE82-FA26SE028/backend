namespace Inventory.BLL.Interfaces;

public interface IInventoryAccess
{
    Task<Guid> RequireAsync(string operation, CancellationToken ct);
}
