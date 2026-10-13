using Shared.Common.Pagination;
using Backend.Contracts.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;

namespace Identity.API.GrpcServices;

public sealed class AuditLogGrpcService(IAuditLogService service) : AuditLogGrpc.AuditLogGrpcBase
{
    public override async Task<AuditLogListReply> ListAuditLogs(ActivityListRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        Guid? userId = request.HasUserId ? ParseId(request.UserId) : null;
        var response = await service.GetAll(actor.UserId, request.Page, request.PageSize, userId, context.CancellationToken);
        var reply = new AuditLogListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };

        if (response.Result is PagedResult<AuditLogDTO> page)
        {
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
            foreach (var item in page.Items)
            {
                var info = new AuditLogInfo
                {
                    AuditLogId = item.AuditLogId.ToString(),
                    CreatedAt = Timestamp.FromDateTime(item.CreatedAt)
                };

                if (item.UserId is { } auditUserId)
                {
                    info.UserId = auditUserId.ToString();
                }

                if (item.Action is { } action)
                {
                    info.Action = action;
                }

                if (item.EntityName is { } entityName)
                {
                    info.EntityName = entityName;
                }

                if (item.EntityId is { } entityId)
                {
                    info.EntityId = entityId;
                }

                if (item.OldValue is { } oldValue)
                {
                    info.OldValue = oldValue;
                }

                if (item.NewValue is { } newValue)
                {
                    info.NewValue = newValue;
                }

                if (item.IpAddress is { } ipAddress)
                {
                    info.IpAddress = ipAddress;
                }

                reply.Items.Add(info);
            }
        }

        return reply;
    }

    private static Guid ParseId(string value)
    {
        return Guid.TryParse(value, out var id) && id != Guid.Empty
        ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user ID."));
    }
}
