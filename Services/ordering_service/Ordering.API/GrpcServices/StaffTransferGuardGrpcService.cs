using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;

namespace Ordering.API.GrpcServices;

public sealed class StaffTransferGuardGrpcService(IStaffTransferGuardService service, IConfiguration configuration)
    : StaffTransferGuardGrpc.StaffTransferGuardGrpcBase
{
    public override async Task<StaffTransferGuardReply> Check(StaffTransferGuardRequest request, ServerCallContext context)
    {
        var expected = configuration["Notifications:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-notification-key");
        if (string.IsNullOrEmpty(expected) || expected.Length < 32 || string.IsNullOrEmpty(supplied)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Không có quyền kiểm tra phân công nội bộ."));
        }

        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhân viên không hợp lệ."));
        }

        return new StaffTransferGuardReply
        {
            HasKitchenAssignment = await service.HasKitchenAssignment(userId, context.CancellationToken)
        };
    }
}
