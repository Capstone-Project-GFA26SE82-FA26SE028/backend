using Backend.Contracts.Identity;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Microsoft.Extensions.Options;

namespace Identity.API.GrpcServices;

public sealed class AccessGrpcService(IUserService users, IOptions<UserManagementSettings> settings) : AccessGrpc.AccessGrpcBase
{
    public override async Task<AdminAccessReply> RequireAdmin(GetMeRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await users.GetMe(actor.UserId, context.CancellationToken);
        if (!response.IsSuccess || response.Result is not UserDTO user || !user.IsActive
            || !settings.Value.AdminRoleIds.Contains(user.RoleId, StringComparer.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Chỉ quản trị viên mới có quyền quản lý nhà hàng."));
        }

        return new AdminAccessReply
        {
            UserId = actor.UserId.ToString()
        };
    }
}
