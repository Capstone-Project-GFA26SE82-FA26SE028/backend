using Shared.Common.DTOs;
using ApiGateway.DTOs;
using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/sessions")]
[RequireBearerToken]
public sealed class SessionController(SessionGrpc.SessionGrpcClient client) : IdentityControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] Guid? userId = null,
        [FromQuery] string? search = null, [FromQuery] string? roleId = null,
        [FromQuery] string? status = null, [FromQuery] string? deviceType = null,
        [FromQuery] DateTimeOffset? fromUtc = null, [FromQuery] DateTimeOffset? toUtc = null)
    {
        return Execute(async () =>
        {
            var request = new ActivityListRequest
            {
                Page = page,
                PageSize = pageSize
            };

            if (userId.HasValue)
            {
                request.UserId = userId.Value.ToString();
            }

            if (search is not null)
            {
                request.Search = search;
            }

            if (roleId is not null)
            {
                request.RoleId = roleId;
            }

            if (status is not null)
            {
                request.Status = status;
            }

            if (deviceType is not null)
            {
                request.DeviceType = deviceType;
            }

            if (fromUtc.HasValue)
            {
                request.FromUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(fromUtc.Value.UtcDateTime);
            }

            if (toUtc.HasValue)
            {
                request.ToUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(toUtc.Value.UtcDateTime);
            }

            var reply = await client.ListSessionsAsync(request, Options(ct));

            return PageResponse(reply);
        }, ct);
    }

    [HttpGet("me")]
    public Task<IActionResult> GetMe(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetMeAsync(new ActivityListRequest
            {
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return PageResponse(reply);
        }, ct);
    }

    [HttpDelete("me/{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.DeleteAsync(new DeleteSessionRequest
            {
                SessionId = id.ToString()
            }, Options(ct));
            return StatusResponse(reply);
        }, ct);
    }

    [HttpDelete("me")]
    public Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.DeleteAllAsync(new LogoutRequest(), Options(ct));
            return StatusResponse(reply);
        }, ct);
    }

    private IActionResult StatusResponse(LogoutReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess
        });
    }

    private IActionResult PageResponse(SessionListReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.IsSuccess ? new
            {
                Items = reply.Items.Select(item => new SessionDTO
                {
                    UserSessionId = Guid.Parse(item.UserSessionId),
                    UserId = Guid.Parse(item.UserId),
                    UserName = item.UserName,
                    Email = item.Email,
                    RoleId = item.RoleId,
                    IpAddress = item.HasIpAddress ? item.IpAddress : null,
                    UserAgent = item.HasUserAgent ? item.UserAgent : null,
                    DeviceType = item.HasDeviceType ? item.DeviceType : null,
                    LoggedInAt = item.LoggedInAt.ToDateTime(),
                    ExpiresAt = item.ExpiresAt.ToDateTime(),
                    RevokedAt = item.RevokedAt?.ToDateTime(),
                    IsCurrent = item.IsCurrent
                }).ToArray(),
                TotalCount = reply.TotalCount,
                Page = reply.Page,
                PageSize = reply.PageSize
            } : null
        });

    }
}
