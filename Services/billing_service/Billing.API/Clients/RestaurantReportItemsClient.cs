using System.Globalization;
using Billing.Common.DTOs;
using Backend.Contracts.Ordering;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class RestaurantReportItemsClient(BillingOrderGrpc.BillingOrderGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings,
    ILogger<RestaurantReportItemsClient> logger) : IRestaurantReportItemsClient
{
    public async Task<IReadOnlyDictionary<Guid, ReportOrderSnapshotDTO>> GetItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var result = new Dictionary<Guid, ReportOrderSnapshotDTO>();
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds);
        foreach (var batch in orderIds.Distinct().Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var request = new OrderReportChannelsRequest
            {
                RestaurantId = restaurantId.ToString()
            };
            request.OrderIds.AddRange(batch.Select(value => value.ToString()));
            ReportOrdersReply reply;
            try
            {
                reply = await client.GetReportItemsAsync(request, new Metadata
                {
                    { "authorization", authorization }
                }, deadline: deadline, cancellationToken: ct);
            }
            catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                or StatusCode.Unimplemented or StatusCode.Internal or StatusCode.ResourceExhausted)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogWarning("Order item reporting dependency unavailable: {Status}", error.StatusCode);
                break;
            }

            if (!Guid.TryParse(reply.RestaurantId, out var owner) || owner != restaurantId
                || reply.Items.Any(value => !Guid.TryParse(value.OrderId, out var id) || !batch.Contains(id))
                || reply.Items.GroupBy(value => Guid.Parse(value.OrderId)).Any(group => group.Count() > 1))
            {
                logger.LogWarning("Invalid order item reporting response.");
                break;
            }

            foreach (var value in reply.Items)
            {
                try
                {
                    var id = Guid.Parse(value.OrderId);
                    var items = value.Items.Select(item => new ReportOrderItemDTO(Guid.Parse(item.OrderItemId),
                        Guid.Parse(item.ItemId), item.Name, decimal.Parse(item.UnitPrice, CultureInfo.InvariantCulture),
                        item.Quantity, item.CancelledQuantity, item.Status,
                        item.Options.Select(option => new BillOptionSnapshot
                        {
                            OptionId = Guid.Parse(option.OptionId),
                            Name = option.Name,
                            Quantity = option.Quantity,
                            ExtraPrice = decimal.Parse(option.ExtraPrice, CultureInfo.InvariantCulture)
                        }).ToArray())).ToArray();
                    if (items.Select(item => item.OrderItemId).Distinct().Count() != items.Length)
                    {
                        continue;
                    }

                    result.Add(id, new ReportOrderSnapshotDTO(id, value.Channel, value.Status, items));
                }
                catch (Exception error) when (error is FormatException or OverflowException)
                {
                    logger.LogWarning("Invalid order item reporting snapshot.");
                }
            }
        }

        return result;
    }
}
