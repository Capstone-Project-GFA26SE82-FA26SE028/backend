using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;

namespace Restaurant.API.GrpcServices;

public sealed class RestaurantAccessGrpcService(ICurrentRestaurantScopeService service, IRestaurantDataScopeService dataScope)
    : RestaurantAccessGrpc.RestaurantAccessGrpcBase
{
    public override async Task<RestaurantDataScopeReply> ResolveDataScope(RestaurantDataScopeRequest request, ServerCallContext context)
    {
        var restaurantId = await dataScope.Resolve(ParseOptional(request.RestaurantId),
            ParseOptional(request.TableSessionId), request.GuestToken, request.AllowCustomer, context.CancellationToken);
        return new RestaurantDataScopeReply
        {
            RestaurantId = restaurantId.ToString()
        };
    }

    private static Guid? ParseOptional(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng hoặc phiên bàn không hợp lệ."));
        }

        return id;
    }

    public override async Task<CurrentRestaurantScopeReply> GetCurrentScope(
        CurrentRestaurantScopeRequest request, ServerCallContext context)
    {
        Guid? restaurantId = null;
        if (request.HasRestaurantId)
        {
            if (!Guid.TryParse(request.RestaurantId, out var parsed) || parsed == Guid.Empty)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng không hợp lệ hoặc bị trống."));
            }

            restaurantId = parsed;
        }

        var scope = await service.GetCurrentScope(restaurantId, context.CancellationToken);
        var reply = new CurrentRestaurantScopeReply
        {
            UserId = scope.UserId.ToString(),
            RoleId = scope.RoleId,
            IsAdministrator = scope.IsAdministrator
        };
        reply.RestaurantIds.AddRange(scope.RestaurantIds.Select(value => value.ToString()));
        return reply;
    }
}
