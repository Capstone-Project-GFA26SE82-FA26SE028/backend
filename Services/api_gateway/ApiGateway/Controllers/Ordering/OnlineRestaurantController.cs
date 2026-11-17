using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/online/restaurants")]
public sealed class OnlineRestaurantController(RestaurantGrpc.RestaurantGrpcClient restaurants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            return BadRequest(new ResponseDTO("Thông tin phân trang không hợp lệ.", 400));
        }

        var reply = await restaurants.BrowseAsync(new ListRequest
        {
            Page = page,
            PageSize = pageSize
        }, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);
        return Ok(new ResponseDTO("OK", 200, true, new
        {
            reply.TotalCount,
            reply.Page,
            reply.PageSize,
            Items = reply.Items.Select(item => new
            {
                item.RestaurantId,
                item.RestaurantName,
                item.Address,
                item.Hotline,
                item.LogoUrl
            })
        }));
    }
}
