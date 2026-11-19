using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class StaffTransferReceiptGrpcService(IStaffTransferReceiptService service, IConfiguration configuration)
    : StaffTransferReceiptGrpc.StaffTransferReceiptGrpcBase
{
    public override async Task<StaffAssignmentTransferReply> Apply(StaffAssignmentTransferRequest request, ServerCallContext context)
    {
        var expected = configuration["StaffProvisioning:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-staff-provisioning-key");
        if (string.IsNullOrEmpty(expected) || expected.Length < 32 || string.IsNullOrEmpty(supplied)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Không có quyền chuyển phân công nội bộ."));
        }

        var result = await service.Apply(new StaffAssignmentTransferDto(Parse(request.TransferId), Parse(request.UserId),
            Parse(request.SourceRestaurantId), Parse(request.TargetRestaurantId)), context.CancellationToken);
        return new StaffAssignmentTransferReply
        {
            Status = result.Status,
            Message = result.Message ?? ""
        };
    }

    private static Guid Parse(string value)
    {
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã chuyển nhân viên không hợp lệ."));
    }
}
