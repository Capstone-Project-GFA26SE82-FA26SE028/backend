using Backend.Contracts.Ordering;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class RestaurantReportOrderClient(BillingOrderGrpc.BillingOrderGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings,
    ILogger<RestaurantReportOrderClient> logger) : IRestaurantReportOrderClient
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var result = new Dictionary<Guid, string>();
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds);
        foreach (var batch in orderIds.Distinct().Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var request = new OrderReportChannelsRequest
            {
                RestaurantId = restaurantId.ToString()
            };
            request.OrderIds.AddRange(batch.Select(value => value.ToString()));
            OrderReportChannelsReply reply;
            try
            {
                reply = await client.GetReportChannelsAsync(request, new Metadata
                {
                    { "authorization", authorization }
                }, deadline: deadline, cancellationToken: ct);
            }
            catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                or StatusCode.Unimplemented or StatusCode.Internal)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogWarning("Order channel reporting dependency unavailable: {Status}", error.StatusCode);
                break;
            }

            if (!Guid.TryParse(reply.RestaurantId, out var owner) || owner != restaurantId
                || reply.Items.Any(value => !Guid.TryParse(value.OrderId, out var id) || !batch.Contains(id))
                || reply.Items.GroupBy(value => Guid.Parse(value.OrderId)).Any(group => group.Count() > 1))
            {
                logger.LogWarning("Invalid order channel reporting response.");
                break;
            }

            foreach (var value in reply.Items.Where(value => value.Channel is "DineIn" or "Takeaway"))
            {
                result.Add(Guid.Parse(value.OrderId), value.Channel);
            }
        }

        return result;
    }
}
