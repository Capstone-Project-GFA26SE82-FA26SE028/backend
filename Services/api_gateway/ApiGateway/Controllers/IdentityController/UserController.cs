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
public sealed class UserController(UserGrpc.UserGrpcClient client) : ControllerBase
{
    private CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (ip is not null)
        {
            headers.Add("x-client-ip", ip);
        }
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(10), ct);
    }
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
        [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        return Execute(async () =>
    {
        var reply = await client.ListUsersAsync(new ListUsersRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search ?? ""
        }, Options(ct));
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

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> call, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await call();
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var (status, message) = ex.StatusCode switch
            {
                Grpc.Core.StatusCode.Unauthenticated => (401, "Token hoặc phiên không hợp lệ."),
                Grpc.Core.StatusCode.PermissionDenied => (403, "Không có quyền truy cập."),
                Grpc.Core.StatusCode.InvalidArgument => (400, "Dữ liệu không hợp lệ."),
                Grpc.Core.StatusCode.NotFound => (404, "Không tìm thấy user."),
                Grpc.Core.StatusCode.Unavailable => (503, "Identity service không khả dụng."),
                Grpc.Core.StatusCode.DeadlineExceeded => (504, "Identity service phản hồi quá thời gian."),
                _ => (502, "Không thể xử lý phản hồi từ Identity service.")
            };
            return StatusCode(status, new ResponseDTO { StatusCode = status, Message = message, IsSuccess = false });
        }
    }




}
