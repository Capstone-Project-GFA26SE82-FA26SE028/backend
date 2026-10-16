using Backend.Contracts.Identity;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Shared.Common.DTOs;

namespace Identity.API.GrpcServices;

public sealed class RoleGrpcService(IRoleService service) : RoleGrpc.RoleGrpcBase
{
    public override async Task<RoleListReply> GetAll(ListRolesRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.GetAll(actor.UserId, context.CancellationToken, request.HasSearch ? request.Search : null);
        return ToListReply(response);
    }

    internal static RoleListReply ToListReply(ResponseDTO response)
    {
        var reply = new RoleListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };
        if (response.Result is IEnumerable<RoleDTO> roles)
        {
            reply.Items.AddRange(roles.Select(Map));
        }

        return reply;
    }

    public override async Task<RoleReply> GetById(RoleIdRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.GetById(actor.UserId, request.RoleId, context.CancellationToken);
        return ToReply(response);
    }

    public override async Task<RoleReply> Create(RoleInfo request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.Create(actor.UserId, new RoleDTO(request.RoleId, request.RoleName, request.HasDescription ? request.Description : null), context.CancellationToken);
        return ToReply(response);
    }

    public override async Task<RoleReply> Update(RoleInfo request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.Update(actor.UserId, request.RoleId, new RoleDTO(request.RoleId, request.RoleName, request.HasDescription ? request.Description : null), context.CancellationToken);
        return ToReply(response);
    }

    public override async Task<RoleReply> Delete(RoleIdRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.Delete(actor.UserId, request.RoleId, context.CancellationToken);
        return ToReply(response);
    }

    private static RoleReply ToReply(ResponseDTO response)
    {
        return new RoleReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess,
            Result = response.Result is RoleDTO role ? Map(role) : null
        };
    }

    private static RoleInfo Map(RoleDTO role)
    {
        var info = new RoleInfo
        {
            RoleId = role.RoleId,
            RoleName = role.RoleName
        };
        if (role.Description is not null)
        {
            info.Description = role.Description;
        }

        return info;
    }
}

