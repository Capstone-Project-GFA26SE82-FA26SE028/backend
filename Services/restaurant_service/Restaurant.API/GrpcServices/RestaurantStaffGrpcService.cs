using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class RestaurantStaffGrpcService(IRestaurantStaffService service, IConfiguration configuration) : RestaurantStaffGrpc.RestaurantStaffGrpcBase
{
    public override async Task<RestaurantStaffInfo> Provision(RestaurantStaffRequest request, ServerCallContext context)
    {
        var expected = configuration["StaffProvisioning:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-staff-provisioning-key");
        if (expected?.Length < 32 || string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Không có quyền phân công nội bộ."));
        }

        return Map(await service.Provision(ParseId(request.RestaurantId), ParseId(request.UserIdRef), context.CancellationToken));
    }

    public override async Task<RestaurantStaffInfo> Create(RestaurantStaffRequest request, ServerCallContext context)
    {
        return Map(await service.Create(ParseId(request.RestaurantId), ParseId(request.UserIdRef), context.CancellationToken));
    }

    public override async Task<RestaurantStaffListReply> GetAll(RestaurantStaffListRequest request, ServerCallContext context)
    {
        var reply = new RestaurantStaffListReply();
        reply.Items.AddRange((await service.GetAll(ParseId(request.RestaurantId), context.CancellationToken)).Select(Map));
        return reply;
    }

    public override async Task<RestaurantStaffInfo> Delete(RestaurantStaffRequest request, ServerCallContext context)
    {
        return Map(await service.Delete(ParseId(request.RestaurantId), ParseId(request.UserIdRef), context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã ID không hợp lệ hoặc bị trống."));
        }

        return id;
    }

    private static RestaurantStaffInfo Map(RestaurantStaffDto value)
    {
        return new RestaurantStaffInfo
        {
            RestaurantId = value.RestaurantId.ToString(),
            UserIdRef = value.UserIdRef.ToString(),
            IsActive = value.IsActive
        };
    }
}
