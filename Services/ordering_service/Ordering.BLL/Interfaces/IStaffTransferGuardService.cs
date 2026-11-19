namespace Ordering.BLL.Interfaces;

public interface IStaffTransferGuardService
{
    Task<bool> HasKitchenAssignment(Guid userId, CancellationToken ct);
}
