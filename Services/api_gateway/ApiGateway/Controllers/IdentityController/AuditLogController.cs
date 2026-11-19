using Shared.Common.DTOs;
using ApiGateway.DTOs;
using ApiGateway.Swagger;
using Backend.Contracts.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/audit-logs")]
[RequireBearerToken]
public sealed class AuditLogController(AuditLogGrpc.AuditLogGrpcClient client) : IdentityControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] Guid? userId = null,
        [FromQuery] string? search = null, [FromQuery] string? action = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null)
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

            if (action is not null)
            {
                request.Action = action;
            }

            request.FromUtc = from.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(from.Value) : null;
            request.ToUtc = to.HasValue ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(to.Value) : null;
            var reply = await client.ListAuditLogsAsync(request, Options(ct));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess ? new
                {
                    Items = reply.Items.Select(item => new AuditLogDTO
                    {
                        AuditLogId = Guid.Parse(item.AuditLogId),
                        FullName = item.HasFullName ? item.FullName : null,
                        UserName = item.HasUserName ? item.UserName : null,
                        Email = item.HasEmail ? item.Email : null,
                        AvatarUrl = item.HasAvatarUrl ? item.AvatarUrl : null,
                        UserId = item.HasUserId ? Guid.Parse(item.UserId) : null,
                        Action = item.HasAction ? item.Action : null,
                        EntityName = item.HasEntityName ? item.EntityName : null,
                        EntityId = item.HasEntityId ? item.EntityId : null,
                        OldValue = item.HasOldValue ? item.OldValue : null,
                        NewValue = item.HasNewValue ? item.NewValue : null,
                        IpAddress = item.HasIpAddress ? item.IpAddress : null,
                        CreatedAt = item.CreatedAt.ToDateTime()
                    }).ToArray(),
                    TotalCount = reply.TotalCount,
                    Page = reply.Page,
                    PageSize = reply.PageSize
                } : null
            });
        }, ct);
    }
}
