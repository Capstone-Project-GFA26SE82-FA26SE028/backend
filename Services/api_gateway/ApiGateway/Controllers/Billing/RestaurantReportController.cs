using System.Globalization;
using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/reports")]
[RequireBearerToken]
public sealed class RestaurantReportController(RestaurantReportGrpc.RestaurantReportGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpGet("period")]
    [EndpointDescription("Cả from và to phải là ISO 8601 có Z/z hoặc offset. Khi viết query URL, encode dấu + thành %2B (ví dụ +07:00 thành %2B07:00), hoặc dùng UTC Z. Không gửi thời gian thiếu offset; máy chủ không tự sửa dấu cách thành dấu +.")]
    [ProducesResponseType(typeof(ResponseDTO<ReportPeriodDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetPeriod(Guid restaurantId, [FromQuery] ReportPeriodQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var reply = await client.GetPeriodAsync(new ReportPeriodRequest
            {
                RestaurantId = restaurantId.ToString(),
                From = query.From ?? string.Empty,
                To = query.To ?? string.Empty,
                GroupBy = query.GroupBy
            }, options);
            return new ReportPeriodDTO(
                Guid.Parse(reply.RestaurantId),
                reply.TimeZone,
                DateTimeOffset.Parse(reply.FromUtc, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reply.ToUtc, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reply.FromLocal, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reply.ToLocal, CultureInfo.InvariantCulture),
                reply.GroupBy,
                reply.WeekStartsOn,
                true,
                true);
        }, ct);
    }
}
