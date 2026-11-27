using System.Security.Cryptography;
using System.Text;
using Backend.Contracts.Identity;
using Grpc.Core;
using Identity.BLL.Interfaces;

namespace Identity.API.GrpcServices;

public sealed class RestaurantApplicantGrpcService(IRestaurantApplicantService service, IConfiguration config)
    : RestaurantApplicantGrpc.RestaurantApplicantGrpcBase
{
    public override async Task<RestaurantApplicantInfo> Reserve(RestaurantApplicantReserveRequest request, ServerCallContext context)
    {
        RequireServiceKey(context);
        Guid? currentUser = null;
        if (!string.IsNullOrWhiteSpace(context.RequestHeaders.GetValue("authorization")))
        {
            currentUser = (await GrpcAuthentication.RequireUser(context)).UserId;
        }

        return await Execute(() => service.Reserve(Id(request.RegistrationId), currentUser, request.Email, request.Password,
            request.FullName, request.PhoneNumber, context.CancellationToken));
    }

    public override async Task<RestaurantApplicantInfo> Activate(RestaurantApplicantActivateRequest request, ServerCallContext context)
    {
        RequireServiceKey(context);
        var reviewer = await GrpcAuthentication.RequireUser(context);
        return await Execute(() => service.Activate(Id(request.RegistrationId), Id(request.UserId), Id(request.RestaurantId),
            request.NewAccount, reviewer.UserId, context.CancellationToken));
    }

    private static async Task<RestaurantApplicantInfo> Execute(Func<Task<RestaurantApplicant>> action)
    {
        try
        {
            return Map(await action());
        }
        catch (RestaurantApplicantException error)
        {
            var status = error.StatusCode switch
            {
                400 => StatusCode.InvalidArgument,
                403 => StatusCode.PermissionDenied,
                404 => StatusCode.NotFound,
                409 => StatusCode.FailedPrecondition,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(status, error.Message));
        }
    }

    private void RequireServiceKey(ServerCallContext context)
    {
        var expected = config["StaffProvisioning:ServiceKey"];
        var actual = context.RequestHeaders.GetValue("x-staff-provisioning-key") ?? "";
        if (expected is null || expected.Length < 32)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Chưa cấu hình dịch vụ đăng ký nhà hàng."));
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Yêu cầu nội bộ không hợp lệ."));
        }
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ID không hợp lệ."));
        }

        return id;
    }

    private static RestaurantApplicantInfo Map(RestaurantApplicant value)
    {
        return new RestaurantApplicantInfo
        {
            UserId = value.UserId.ToString(),
            Email = value.Email
        };
    }
}
