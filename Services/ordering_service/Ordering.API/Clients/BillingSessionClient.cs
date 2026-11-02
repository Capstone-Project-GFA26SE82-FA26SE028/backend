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
            throw new OrderingException(401, "Yêu cầu phiên làm việc của nhân viên hợp lệ.");
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
            throw new OrderingException(503, "Dịch vụ nhà hàng trả về phiên thanh toán không hợp lệ.");
        }

        return branch;
    }
}
