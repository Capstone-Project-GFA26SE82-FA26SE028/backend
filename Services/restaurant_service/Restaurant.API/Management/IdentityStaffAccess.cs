using Backend.Contracts.Identity;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class IdentityStaffAccess(UserGrpc.UserGrpcClient users, IManagementAccess management,
    IHttpContextAccessor accessor) : IStaffIdentity
{
    public async Task<StaffActorDto> GetCurrentAsync(CancellationToken ct)
    {
        var reply = await users.GetMeAsync(new GetMeRequest(), Options(ct));
        var actor = Parse(reply);
        try
        {
            await management.RequireAdmin(ct);
            actor.IsAdministrator = true;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.PermissionDenied)
        {
            actor.IsAdministrator = false;
        }
        catch (ManagementException exception) when (exception.StatusCode == 403)
        {
            actor.IsAdministrator = false;
        }

        return actor;
    }

    public async Task<StaffActorDto> GetUserAsync(Guid userId, CancellationToken ct)
    {
        return Parse(await users.GetUserAsync(new GetUserRequest
        {
            UserId = userId.ToString()
        }, Options(ct)));
    }

    private CallOptions Options(CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new ManagementException(401, "Yêu cầu phiên làm việc của nhân viên hợp lệ.");
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(5), ct);
    }

    private static StaffActorDto Parse(UserReply reply)
    {
        if (!reply.IsSuccess || reply.Result is null)
        {
            var status = reply.StatusCode is 400 or 401 or 403 or 404 ? reply.StatusCode : 503;
            throw new ManagementException(status, "Hệ thống xác thực không thể xác minh tài khoản nhân viên.");
        }

        if (!reply.Result.IsActive || !Guid.TryParse(reply.Result.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new ManagementException(403, "Yêu cầu tài khoản nhân viên đang hoạt động.");
        }

        return new StaffActorDto
        {
            UserId = userId,
            RoleId = reply.Result.RoleId
        };
    }
}
