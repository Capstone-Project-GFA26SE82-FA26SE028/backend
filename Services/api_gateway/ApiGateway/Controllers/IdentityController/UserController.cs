using Shared.Common.Pagination;
using Shared.Common.DTOs;
using ApiGateway.DTOs.Identity;
using ApiGateway.DTOs;
using System.ComponentModel.DataAnnotations;
using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/users")]
[RequireBearerToken]
public sealed class UserController(UserGrpc.UserGrpcClient client) : IdentityControllerBase
{
    [HttpGet("me")]
    public Task<IActionResult> GetMe(CancellationToken ct)
    {
        return Execute(async () =>
        UserResponse(await client.GetMeAsync(new GetMeRequest(), Options(ct))), ct);
    }

    [HttpPut("me")]
    public Task<IActionResult> UpdateMe(UpdateProfileDTO body, CancellationToken ct)
    {
        return Execute(async () =>
    {
        var request = new UpdateMeRequest { FullName = body.FullName };
        if (body.PhoneNumber is not null)
        {
            request.PhoneNumber = body.PhoneNumber;
        }

        if (body.AvatarUrl is not null)
        {
            request.AvatarUrl = body.AvatarUrl;
        }

        return UserResponse(await client.UpdateMeAsync(request, Options(ct)));
    }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] string? roleId = null, [FromQuery] bool? isActive = null)
    {
        return Execute(async () =>
    {
        var request = new ListUsersRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search ?? ""
        };
        if (roleId is not null)
        {
            request.RoleId = roleId;
        }

        if (isActive.HasValue)
        {
            request.IsActive = isActive.Value;
        }

        var reply = await client.ListUsersAsync(request, Options(ct));
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.IsSuccess ? new PagedResult<UserDTO>
            {
                Items = reply.Items.Select(MapUser).ToArray(),
                TotalCount = reply.TotalCount,
                Page = reply.Page,
                PageSize = reply.PageSize
            } : null
        });
    }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        UserResponse(await client.GetUserAsync(new GetUserRequest { UserId = id.ToString() }, Options(ct))), ct);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateUserDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new UpdateUserRequest
            {
                UserId = id.ToString(),
                UserName = body.UserName,
                Email = body.Email,
                FullName = body.FullName
            };
            if (body.PhoneNumber is not null)
            {
                request.PhoneNumber = body.PhoneNumber;
            }

            if (body.AvatarUrl is not null)
            {
                request.AvatarUrl = body.AvatarUrl;
            }

            return UserResponse(await client.UpdateUserAsync(request, Options(ct)));
        }, ct);
    }

    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> SetStatus(Guid id, StatusDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        UserResponse(await client.SetUserStatusAsync(new SetUserStatusRequest
        {
            UserId = id.ToString(),
            IsActive = body.IsActive!.Value
        }, Options(ct))), ct);
    }

    [HttpPost]
    public Task<IActionResult> Create(CreateUserDTO body, CancellationToken ct)
    {
        return Execute(async () =>
    {
        var request = new CreateUserRequest
        {
            UserName = body.UserName,
            Email = body.Email,
            Password = body.Password,
            FullName = body.FullName,
            RoleId = body.RoleId
        };
        if (body.PhoneNumber is not null)
        {
            request.PhoneNumber = body.PhoneNumber;
        }

        if (body.AvatarUrl is not null)
        {
            request.AvatarUrl = body.AvatarUrl;
        }

        var reply = await client.CreateUserAsync(request, Options(ct));
        if (reply.StatusCode == 201 && reply.Result is not null)
        {
            Response.Headers.Location = $"/api/users/{reply.Result.UserId}";
        }

        return UserResponse(reply);
    }, ct);
    }

    [HttpPatch("{id:guid}/role")]
    public Task<IActionResult> SetRole(Guid id, RoleDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        UserResponse(await client.SetUserRoleAsync(new SetUserRoleRequest
        {
            UserId = id.ToString(),
            RoleId = body.RoleId
        }, Options(ct))), ct);
    }

    private IActionResult UserResponse(UserReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : MapUser(reply.Result)
        });
    }

    private static UserDTO MapUser(UserInfo user)
    {
        return new UserDTO
        {
            UserId = user.UserId,
            RoleId = user.RoleId,
            UserName = user.UserName,
            Email = user.Email,
            IsActive = user.IsActive,
            FullName = user.HasFullName ? user.FullName : null,
            PhoneNumber = user.HasPhoneNumber ? user.PhoneNumber : null,
            AvatarUrl = user.HasAvatarUrl ? user.AvatarUrl : null,
            CreatedAt = user.CreatedAt?.ToDateTime(),
            UpdatedAt = user.UpdatedAt?.ToDateTime()
        };
    }
}
