using Shared.Common.Pagination;
using Shared.Common.DTOs;
using Backend.Contracts.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;

namespace Identity.API.GrpcServices;

public sealed class UserGrpcService(IUserService users, IRoleService roles) : UserGrpc.UserGrpcBase
{
    public override async Task<UserReply> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.Update(actor.UserId, ParseId(request.UserId), new UpdateUserDTO
        {
            UserName = request.UserName,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.HasPhoneNumber ? request.PhoneNumber : null,
            AvatarUrl = request.HasAvatarUrl ? request.AvatarUrl : null
        }, context.CancellationToken));
    }

    public override async Task<UserReply> GetMe(GetMeRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.GetMe(actor.UserId, context.CancellationToken));
    }

    public override async Task<UserReply> UpdateMe(UpdateMeRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.UpdateMe(actor.UserId, new UpdateProfileDTO
        {
            FullName = request.FullName,
            PhoneNumber = request.HasPhoneNumber ? request.PhoneNumber : null,
            AvatarUrl = request.HasAvatarUrl ? request.AvatarUrl : null
        }, context.CancellationToken));
    }

    public override async Task<UserReply> GetUser(GetUserRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.GetById(actor.UserId, ParseId(request.UserId), context.CancellationToken));
    }

    public override async Task<UserReply> SetUserStatus(SetUserStatusRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.SetStatus(actor.UserId, ParseId(request.UserId), request.IsActive, context.CancellationToken));
    }

    public override async Task<UserListReply> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await users.GetAll(actor.UserId, request.Page, request.PageSize, request.Search, context.CancellationToken, new UserFilter
        {
            RoleId = request.HasRoleId ? request.RoleId : null,
            IsActive = request.HasIsActive ? request.IsActive : null
        });
        var reply = new UserListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<UserDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(MapUser));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }
        return reply;
    }

    public override async Task<UserReply> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.Create(actor.UserId, new CreateUserDTO
        {
            UserName = request.UserName,
            Email = request.Email,
            Password = request.Password,
            AvatarUrl = request.HasAvatarUrl ? request.AvatarUrl : null,
            FullName = request.FullName,
            PhoneNumber = request.HasPhoneNumber ? request.PhoneNumber : null,
            RoleId = request.RoleId
        }, context.CancellationToken));
    }

    public override async Task<UserReply> SetUserRole(SetUserRoleRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await users.SetRole(actor.UserId, ParseId(request.UserId), request.RoleId, context.CancellationToken));
    }

    public override async Task<RoleListReply> ListRoles(ListRolesRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await roles.GetAll(actor.UserId, context.CancellationToken, request.HasSearch ? request.Search : null);
        return RoleGrpcService.ToListReply(response);
    }
    private static Guid ParseId(string value)
    {
        return Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user ID."));
    }

    private static UserReply Map(ResponseDTO response)
    {
        return new()
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess,
            Result = response.Result is UserDTO user ? MapUser(user) : null
        };
    }

    private static UserInfo MapUser(UserDTO user)
    {
        var info = new UserInfo
        {
            UserId = user.UserId.ToString(),
            RoleId = user.RoleId,
            UserName = user.UserName,
            Email = user.Email,
            IsActive = user.IsActive,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc))
        };
        if (user.FullName is not null)
        {
            info.FullName = user.FullName;
        }

        if (user.PhoneNumber is not null)
        {
            info.PhoneNumber = user.PhoneNumber;
        }

        if (user.AvatarUrl is not null)
        {
            info.AvatarUrl = user.AvatarUrl;
        }

        if (user.UpdatedAt is { } updated)
        {
            info.UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(updated, DateTimeKind.Utc));
        }

        return info;
    }
}
