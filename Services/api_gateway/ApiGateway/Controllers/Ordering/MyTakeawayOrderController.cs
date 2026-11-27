using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/customers/me/takeaway-orders")]
[RequireBearerToken]
public sealed class MyTakeawayOrderController(OrderGrpc.OrderGrpcClient ordering,
    RestaurantGrpc.RestaurantGrpcClient restaurants, IOptions<OrderingClientSettings> settings)
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

            var items = reply.Items.Select(value =>
            {
                var order = OrderingMapping.Map(value);
                return new MyTakeawayOrderDTO
                {
                    OrderId = order.OrderId,
                    RestaurantId = order.RestaurantId,
                    RestaurantName = names.Names.GetValueOrDefault(value.RestaurantId),
                    CreatedAt = order.CreatedAt,
                    Status = order.Status,
                    PaymentStatus = order.PaymentStatus,
                    TotalAmount = order.TotalAmount,
                    Items = order.Items
                };
            }).ToArray();
            return Result(new PagedResult<MyTakeawayOrderDTO>(items, reply.TotalCount, page, pageSize));
        }, ct);
    }
}
