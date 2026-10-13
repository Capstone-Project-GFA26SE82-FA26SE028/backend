using Shared.Common.Pagination;
using Backend.Contracts.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;

namespace Identity.API.GrpcServices;

public sealed class SessionGrpcService(ISessionService service) : SessionGrpc.SessionGrpcBase
{
    public override async Task<SessionListReply> ListSessions(ActivityListRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        Guid? userId = request.HasUserId ? ParseId(request.UserId) : null;
        var response = await service.GetAll(actor.UserId, request.Page, request.PageSize, userId, context.CancellationToken);
        return ToReply(response, actor.SessionId);
    }

    public override async Task<SessionListReply> GetMe(ActivityListRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.GetMe(actor.UserId, request.Page, request.PageSize, context.CancellationToken);
        return ToReply(response, actor.SessionId);
    }

    public override async Task<LogoutReply> Delete(DeleteSessionRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.Delete(actor.UserId, ParseId(request.SessionId), context.CancellationToken);
        return StatusReply(response);
    }

    public override async Task<LogoutReply> DeleteAll(LogoutRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await service.DeleteAll(actor.UserId, context.CancellationToken);
        return StatusReply(response);
    }

    private static LogoutReply StatusReply(Shared.Common.DTOs.ResponseDTO response)
    {
        return new LogoutReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };
    }

    private static SessionListReply ToReply(Shared.Common.DTOs.ResponseDTO response, Guid currentSessionId)
    {
        var reply = new SessionListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };

        if (response.Result is PagedResult<SessionDTO> page)
        {
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
            foreach (var item in page.Items)
            {
                var info = new SessionInfo
                {
                    UserSessionId = item.UserSessionId.ToString(),
                    UserId = item.UserId.ToString(),
                    LoggedInAt = Timestamp.FromDateTime(item.LoggedInAt),
                    ExpiresAt = Timestamp.FromDateTime(item.ExpiresAt),
                    IsCurrent = item.UserSessionId == currentSessionId
                };

                if (item.IpAddress is { } ipAddress)
                {
                    info.IpAddress = ipAddress;
                }

                if (item.UserAgent is { } userAgent)
                {
                    info.UserAgent = userAgent;
                }

                if (item.DeviceType is { } deviceType)
                {
                    info.DeviceType = deviceType;
                }

                if (item.RevokedAt is { } revokedAt)
                {
                    info.RevokedAt = Timestamp.FromDateTime(revokedAt);
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
