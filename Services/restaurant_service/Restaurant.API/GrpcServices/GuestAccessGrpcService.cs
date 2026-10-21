using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.GrpcServices;

public sealed class GuestAccessGrpcService(IGuestAccessService service) : GuestAccessGrpc.GuestAccessGrpcBase
{
    public override async Task<GuestAccessReply> Join(GuestJoinRequest request, ServerCallContext context)
    {
        return Map(await service.Join(ParseId(request.TableSessionId), request.PhoneOrCode, context.CancellationToken));
    }

    public override async Task<GuestAccessReply> GetMe(GuestAccessRequest request, ServerCallContext context)
    {
        return Map(await service.GetMe(ParseId(request.TableSessionId), request.Token, context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new ManagementException(400, "Invalid table session ID.");
        }

        return id;
    }

    private static GuestAccessReply Map(GuestAccessDto result)
    {
        return new GuestAccessReply
        {
            GuestAccessId = result.GuestAccessId.ToString(),
            TableSessionId = result.TableSessionId.ToString(),
            ExpiresAt = result.ExpiresAt.ToString("O"),
            Token = result.Token
        };
    }
}
