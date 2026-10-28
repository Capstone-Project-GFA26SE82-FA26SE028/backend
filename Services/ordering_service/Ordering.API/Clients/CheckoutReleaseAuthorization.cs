using Backend.Contracts.Billing;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class CheckoutReleaseAuthorization(BillCheckoutGrpc.BillCheckoutGrpcClient client,
    IHttpContextAccessor http, IOptions<OrderingSettings> settings) : ICheckoutReleaseAuthorization
{
    public async Task RequireAsync(Guid sessionId, Guid checkoutId, CancellationToken ct)
    {
        await client.AuthorizeReleaseAsync(new BillCheckoutRequest
        {
            TableSessionId = sessionId.ToString(),
            CheckoutId = checkoutId.ToString()
        }, new Metadata
        {
            { "authorization", http.HttpContext!.Request.Headers.Authorization.ToString() }
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
    }
}
