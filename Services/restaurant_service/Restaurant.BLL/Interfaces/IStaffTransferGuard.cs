namespace Restaurant.BLL.Interfaces;

public interface IStaffTransferGuard
{
    Task<bool> HasKitchenAssignmentAsync(Guid userId, CancellationToken ct);
}
