using System.Collections.Concurrent;
using System.Globalization;
using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Backend.Contracts.Ordering;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/customers/me/takeaway-orders")]
[RequireBearerToken]
public sealed class MyTakeawayOrderController(OrderGrpc.OrderGrpcClient ordering,
    RestaurantGrpc.RestaurantGrpcClient restaurants, MenuGrpc.MenuGrpcClient catalog,
    IOptions<OrderingClientSettings> settings, ILogger<MyTakeawayOrderController> logger)
    : OrderingControllerBase(settings)
{
    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<MyTakeawayOrderDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll(CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery] string? status = null)
    {
        return Execute(async () =>
        {
            var request = new MyTakeawayOrdersRequest
            {
                Page = page,
                PageSize = pageSize
            };
            if (status is not null)
            {
                request.Status = status;
            }

            var options = Options(ct);
            var reply = await ordering.GetMyTakeawayOrdersAsync(request, options);
            var lookup = new RestaurantNamesRequest();
            lookup.RestaurantIds.AddRange(reply.Items.Select(order => order.RestaurantId)
                .Where(id => id.Length > 0).Distinct());

            var names = new RestaurantNamesReply();
            if (lookup.RestaurantIds.Count > 0)
            {
                names = await restaurants.GetNamesAsync(lookup,
                    deadline: options.Deadline, cancellationToken: ct);
            }

            var images = await GetItemImages(reply.Items, names, options, ct);
            var items = reply.Items.Select(order => Map(order, names, images.GetValueOrDefault(order.RestaurantId))).ToArray();
            return Result(new PagedResult<MyTakeawayOrderDTO>(items, reply.TotalCount, page, pageSize));
        }, ct);
    }

    private async Task<ConcurrentDictionary<string, MenuItemImagesReply>> GetItemImages(IEnumerable<OrderInfo> orders,
        RestaurantNamesReply names, CallOptions options, CancellationToken ct)
    {
        var groups = orders.Where(order => names.Names.ContainsKey(order.RestaurantId) && order.Items.Count > 0)
            .GroupBy(order => order.RestaurantId);
        var images = new ConcurrentDictionary<string, MenuItemImagesReply>();
        var authorization = Request.Headers.Authorization.ToString();
        await Parallel.ForEachAsync(groups, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 8
        }, async (group, cancellationToken) =>
        {
            var request = new MenuItemImagesRequest
            {
                RestaurantId = group.Key
            };
            request.ItemIds.AddRange(group.SelectMany(order => order.Items).Select(item => item.ItemId).Distinct());
            var headers = new Metadata
            {
                { "authorization", authorization }
            };
            try
            {
                images[group.Key] = await catalog.GetItemImagesAsync(request, headers,
                    deadline: options.Deadline, cancellationToken: cancellationToken);
            }
            catch (RpcException error) when (!cancellationToken.IsCancellationRequested
                && error.StatusCode is Grpc.Core.StatusCode.NotFound or Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded)
            {
                logger.LogWarning("Ảnh món từ Catalog không khả dụng cho nhà hàng {RestaurantId}. Mã gRPC {StatusCode}.",
                    group.Key, error.StatusCode);
            }
        });
        return images;
    }

    private static MyTakeawayOrderDTO Map(OrderInfo order, RestaurantNamesReply names, MenuItemImagesReply? images)
    {
        return new MyTakeawayOrderDTO
        {
            OrderId = Guid.Parse(order.OrderId),
            RestaurantId = Guid.TryParse(order.RestaurantId, out var restaurantId) ? restaurantId : null,
            RestaurantName = names.Names.GetValueOrDefault(order.RestaurantId),
            CreatedAt = DateTimeOffset.Parse(order.CreatedAt, CultureInfo.InvariantCulture),
            Status = order.Status,
            PaymentStatus = string.IsNullOrEmpty(order.PaymentStatus) ? null : order.PaymentStatus,
            TotalAmount = decimal.Parse(order.TotalAmount, CultureInfo.InvariantCulture),
            Items = order.Items.Select(item =>
            {
                var result = OrderingMapping.MapOrderLine<MyTakeawayOrderItemDTO>(item);
                result.ImageUrl = images?.ImageUrls.GetValueOrDefault(item.ItemId);
                return result;
            }).ToList()
        };
    }
}
