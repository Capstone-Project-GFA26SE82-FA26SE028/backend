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
        [FromQuery] int pageSize = 20, [FromQuery] Guid? userId = null)
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
