using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class BillingSessionClient(BillingSessionGrpc.BillingSessionGrpcClient client,
    IHttpContextAccessor http, IOptions<OrderingSettings> settings) : IBillingSessionClient
{
    public async Task<Guid> GetRestaurantIdAsync(Guid sessionId, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "A valid employee session is required.");
        }

        var reply = await client.GetByIdAsync(new BillingSessionRequest
        {
            TableSessionId = sessionId.ToString()
        }, new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(reply.TableSessionId, out var returnedId) || returnedId != sessionId
            || !Guid.TryParse(reply.RestaurantId, out var branch) || branch == Guid.Empty)
        {
            throw new OrderingException(503, "The restaurant service returned an invalid billing session.");
        }

        return branch;
    }
}
