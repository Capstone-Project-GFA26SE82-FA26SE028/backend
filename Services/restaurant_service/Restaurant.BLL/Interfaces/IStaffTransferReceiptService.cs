using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IStaffTransferReceiptService
{
    Task<StaffAssignmentTransferResult> Apply(StaffAssignmentTransferDto request, CancellationToken ct);
}
