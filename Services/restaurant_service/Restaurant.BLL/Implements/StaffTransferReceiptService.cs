using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.BLL.Implements;

public sealed class StaffTransferReceiptService(IStaffTransferReceiptRepository receipts,
    IRestaurantStaffRepository staff, IRestaurantRepository restaurants, IServiceRequestRepository requests,
    IManagementTransaction transaction, IStaffTransferGuard guard) : IStaffTransferReceiptService
{
    public async Task<StaffAssignmentTransferResult> Apply(StaffAssignmentTransferDto request, CancellationToken ct)
    {
        // Identity has disabled the user before requesting the transfer.
        // Do not hold Restaurant locks while waiting for Ordering's assignment lock.
        var hasKitchenAssignment = await guard.HasKitchenAssignmentAsync(request.UserId, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var assignments = await staff.LockUserAssignmentsAsync(request.UserId, ct);
            var previous = await receipts.GetByIdAsync(request.Id, ct);
            if (previous is not null)
            {
                if (previous.UserId != request.UserId || previous.SourceRestaurantId != request.SourceRestaurantId
                    || previous.TargetRestaurantId != request.TargetRestaurantId)
                {
                    throw new ManagementException(409, "Mã chuyển nhân viên đã được sử dụng cho dữ liệu khác.");
                }

                return new StaffAssignmentTransferResult(previous.Status, previous.Message);
            }

            string? rejection = null;
            if (request.SourceRestaurantId == request.TargetRestaurantId
                || await restaurants.GetByIdAsync(request.TargetRestaurantId, ct) is null)
            {
                rejection = "Nhà hàng đích không tồn tại hoặc trùng nhà hàng hiện tại.";
            }
            else if (!assignments.Any(item => item.RestaurantId == request.SourceRestaurantId)
                || assignments.Any(item => item.IsActive && item.RestaurantId != request.SourceRestaurantId))
            {
                rejection = "Phân công hiện tại không khớp với nhà hàng nguồn; cần kiểm tra dữ liệu nhân viên.";
            }
            else if (await requests.HasUnfinishedForStaffAsync(request.UserId, ct))
            {
                rejection = "Nhân viên còn yêu cầu phục vụ hoặc thanh toán đang phụ trách. Cần hoàn thành trước khi chuyển.";
            }
            else if (hasKitchenAssignment)
            {
                rejection = "Nhân viên còn được phân công trạm bếp. Cần bàn giao trạm bếp trước khi chuyển.";
            }

            if (rejection is null)
            {
                foreach (var assignment in assignments)
                {
                    assignment.IsActive = false;
                }

                var destination = assignments.FirstOrDefault(item => item.RestaurantId == request.TargetRestaurantId);
                if (destination is null)
                {
                    await staff.AddAsync(new RestaurantStaff
                    {
                        RestaurantId = request.TargetRestaurantId,
                        UserIdRef = request.UserId,
                        IsActive = true
                    });
                }
                else
                {
                    destination.IsActive = true;
                }
            }

            var receipt = new StaffTransferReceipt
            {
                StaffTransferReceiptId = request.Id,
                UserId = request.UserId,
                SourceRestaurantId = request.SourceRestaurantId,
                TargetRestaurantId = request.TargetRestaurantId,
                Status = rejection is null ? "Completed" : "Rejected",
                Message = rejection,
                CreatedAt = DateTime.UtcNow
            };
            receipts.Create(receipt);
            return new StaffAssignmentTransferResult(receipt.Status, receipt.Message);
        }, ct);
    }
}
