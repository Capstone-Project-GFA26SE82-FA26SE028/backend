using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;

namespace Restaurant.API.GrpcServices;

public sealed class BillingSessionGrpcService(IBillingSessionService service) : BillingSessionGrpc.BillingSessionGrpcBase
{
    public override async Task<BillingSessionReply> GetById(BillingSessionRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã phiên bàn không hợp lệ hoặc bị trống."));
        }

        var result = await service.GetById(id, context.CancellationToken);
        return new BillingSessionReply
        {
            TableSessionId = result.TableSessionId.ToString(),
            RestaurantId = result.RestaurantId.ToString()
        };
    }
}
