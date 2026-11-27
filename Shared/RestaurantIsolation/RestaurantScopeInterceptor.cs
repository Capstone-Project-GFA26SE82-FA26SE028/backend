using Backend.Contracts.Restaurant;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Shared.Persistence.RestaurantIsolation;

namespace RestaurantIsolation;

public sealed class RestaurantScopeInterceptor(RestaurantAccessGrpc.RestaurantAccessGrpcClient restaurants,
    RestaurantDataScope scope, IHttpContextAccessor http) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // These entry points establish scope after checking their service key or webhook signature,
        // or serve public/customer lookups that do not require staff session.
        if (context.Method is "/billing.OnlinePaymentGrpc/Execute" or "/billing.PaymentWebhookGrpc/Process"
            or "/billing.PaymentSettlementGrpc/GetVerifiedCheckout"
            or "/customer.FeedbackGrpc/Submit" or "/ordering.StaffTransferGuardGrpc/Check"
            or "/customer.CustomerGrpc/GetByPhone" or "/customer.CustomerGrpc/GetById"
            or "/customer.CustomerGrpc/GetOrCreate" or "/customer.CustomerGrpc/GetTiers"
            or "/customer.CustomerGrpc/EarnPoints" or "/customer.CustomerGrpc/ReservePoints"
            or "/customer.CustomerGrpc/CommitPoints" or "/customer.CustomerGrpc/CancelPoints"
            or "/customer.FeedbackGrpc/CreateAccess")
        {
            return await continuation(request, context);
        }

        // This history read checks the authenticated customer and filters by customer ID across restaurants.
        if (context.Method == "/ordering.OrderGrpc/GetMyTakeawayOrders")
        {
            return await continuation(request, context);
        }

        var message = request as IMessage;
        var access = message?.Descriptor.FindFieldByName("access")?.Accessor.GetValue(message) as IMessage;
        var explicitRestaurant = Text(message, "restaurant_id");
        var header = context.RequestHeaders.GetValue("x-restaurant-id") ?? "";
        if (explicitRestaurant.Length > 0 && header.Length > 0
            && (!Guid.TryParse(explicitRestaurant, out var bodyId) || !Guid.TryParse(header, out var headerId) || bodyId != headerId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Nhà hàng trong yêu cầu không khớp với nhà hàng đang chọn."));
        }

        var method = context.Method;
        var allowCustomer = method.StartsWith("/catalog.MenuGrpc/", StringComparison.Ordinal)
            || method.StartsWith("/catalog.OrderValidationGrpc/", StringComparison.Ordinal)
            || method.StartsWith("/ordering.CartGrpc/", StringComparison.Ordinal)
            || method.StartsWith("/ordering.OrderGrpc/", StringComparison.Ordinal)
            || method.StartsWith("/ordering.CancellationRequestGrpc/", StringComparison.Ordinal)
            || method == "/ordering.CheckoutLockGrpc/Verify"
            || method is "/billing.BillCheckoutGrpc/Request" or "/billing.BillCheckoutGrpc/GetCurrent";
        var session = Text(access, "table_session_id");
        if (session.Length == 0)
        {
            session = Text(message, "table_session_id");
        }

        var headers = new Metadata();
        var authorization = context.RequestHeaders.GetValue("authorization");
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        var reply = await restaurants.ResolveDataScopeAsync(new RestaurantDataScopeRequest
        {
            RestaurantId = explicitRestaurant.Length > 0 ? explicitRestaurant : header,
            TableSessionId = session,
            GuestToken = access is null ? Text(message, "guest_token") : Text(access, "guest_token"),
            AllowCustomer = allowCustomer
        }, headers, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: context.CancellationToken);
        if (!Guid.TryParse(reply.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Không thể xác minh nhà hàng."));
        }

        scope.Select(restaurantId);
        // Forward only the server-verified scope to downstream services.
        http.HttpContext!.Request.Headers["x-restaurant-id"] = restaurantId.ToString();
        try
        {
            return await continuation(request, context);
        }
        catch (RestaurantScopeException error)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, error.Message));
        }
    }

    private static string Text(IMessage? message, string name)
    {
        return message?.Descriptor.FindFieldByName(name)?.Accessor.GetValue(message) as string ?? "";
    }
}
