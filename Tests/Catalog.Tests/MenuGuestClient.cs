using Backend.Contracts.Restaurant;
using Grpc.Core;

namespace Catalog.Tests;

internal sealed class MenuGuestClient : GuestAccessGrpc.GuestAccessGrpcClient
{
    public static Guid SessionId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static string Token { get; } = new string('A', 64);

    public override AsyncUnaryCall<GuestAccessReply> GetMeAsync(GuestAccessRequest request, CallOptions options)
    {
        if (request.Token == new string('C', 64))
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Restaurant is unavailable."));
        }

        if (request.Token != Token || request.TableSessionId != SessionId.ToString())
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Expired, closed or invalid session."));
        }

        return new AsyncUnaryCall<GuestAccessReply>(Task.FromResult(new GuestAccessReply
        {
            TableSessionId = SessionId.ToString(),
            ExpiresAt = ItemTestApplication.Now.AddHours(1).ToString("O")
        }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
        {
        });
    }
}
