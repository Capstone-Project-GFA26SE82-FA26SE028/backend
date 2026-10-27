using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class CheckoutAccess(IOrderingAccess guests, IBillingSessionClient sessions,
    RestaurantAccessGrpc.RestaurantAccessGrpcClient staff, IHttpContextAccessor http,
    IOptions<OrderingSettings> settings) : ICheckoutAccess
{
    public async Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(guestToken))
        {
            await guests.RequireAsync(sessionId, null, guestToken, ct);
            return;
        }

        var branch = await sessions.GetRestaurantIdAsync(sessionId, ct);
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "A valid employee session or QR guest session is required.");
        }

        var actor = await staff.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = branch.ToString()
        }, new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(actor.UserId, out var userId) || userId == Guid.Empty || string.IsNullOrWhiteSpace(actor.RoleId))
        {
            throw new OrderingException(503, "The restaurant service returned an invalid employee scope.");
        }

        if (write && (actor.IsAdministrator || !settings.Value.CheckoutCashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(branch.ToString())))
        {
            throw new OrderingException(403, "Only an assigned Cashier can manage checkout.");
        }
    }
}
