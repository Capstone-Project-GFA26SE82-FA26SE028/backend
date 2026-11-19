using Ordering.BLL.Interfaces;
using Ordering.DAL.Interfaces;

namespace Ordering.BLL.Implements;

public sealed class StaffTransferGuardService(IKitchenAssignmentRepository assignments) : IStaffTransferGuardService
{
    public Task<bool> HasKitchenAssignment(Guid userId, CancellationToken ct)
    {
        return assignments.ExistsForUserAsync(userId, ct);
    }
}
