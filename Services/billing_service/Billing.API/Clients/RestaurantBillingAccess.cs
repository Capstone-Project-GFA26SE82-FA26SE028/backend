using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class RestaurantBillingAccess(
    RestaurantAccessGrpc.RestaurantAccessGrpcClient client,
    IHttpContextAccessor accessor,
    IOptions<RestaurantAccessSettings> settings) : IBillingAccess
{
    public async Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        var request = new CurrentRestaurantScopeRequest();
        if (restaurantId.HasValue)
        {
            request.RestaurantId = restaurantId.Value.ToString();
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var reply = await client.GetCurrentScopeAsync(request, headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.UserId, out var userId) || userId == Guid.Empty
            || string.IsNullOrWhiteSpace(reply.RoleId)
            || reply.RestaurantIds.Any(id => !Guid.TryParse(id, out var value) || value == Guid.Empty))
        {
            throw new BillingException(503, "The restaurant service returned an invalid employee scope.");
        }

        return new BillingAccessScope
        {
            UserId = userId,
            RoleId = reply.RoleId,
            IsAdministrator = reply.IsAdministrator,
            RestaurantIds = reply.RestaurantIds.Select(Guid.Parse).Distinct().ToArray()
        };
    }
}
