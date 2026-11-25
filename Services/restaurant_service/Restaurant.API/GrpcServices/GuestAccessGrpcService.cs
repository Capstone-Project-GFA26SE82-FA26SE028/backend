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
        Guid? customerId = request.HasCustomerId && Guid.TryParse(request.CustomerId, out var cId) ? cId : null;
        return Map(await service.Join(ParseId(request.TableSessionId), request.PhoneOrCode, customerId, context.CancellationToken));
    }

    public override async Task<GuestAccessReply> GetMe(GuestAccessRequest request, ServerCallContext context)
    {
        return Map(await service.GetMe(ParseId(request.TableSessionId), request.Token, context.CancellationToken));
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new ManagementException(400, "Mã phiên bàn không hợp lệ.");
        }

        return id;
    }

    private static GuestAccessReply Map(GuestAccessDto result)
    {
        var reply = new GuestAccessReply
        {
            GuestAccessId = result.GuestAccessId.ToString(),
            TableSessionId = result.TableSessionId.ToString(),
            ExpiresAt = result.ExpiresAt.ToString("O"),
            Token = result.Token
        };
        if (result.CustomerId.HasValue)
        {
            reply.CustomerId = result.CustomerId.Value.ToString();
        }
        return reply;
    }
}
