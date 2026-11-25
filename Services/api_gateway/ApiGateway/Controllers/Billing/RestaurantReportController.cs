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
    [HttpGet("dishes")]
    [EndpointDescription("Bảng món theo kỳ thanh toán, lượng hợp lệ sau hủy/từ chối, sắp giảm dần knownQuantity. itemId chỉ lọc bảng món, không đổi tổng thu/hoàn của API sales. CogsSnapshot là giá vốn tổng dòng. Trường null là thiếu dữ liệu, không phải 0. from/to cần offset; encode + thành %2B.")]
    [ProducesResponseType(typeof(ResponseDTO<RestaurantDishReportDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetDishes(Guid restaurantId, [FromQuery] ReportPeriodQueryDTO query,
        [FromQuery] SalesReportFilterDTO filters, [FromQuery] Guid? itemId, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new DishReportRequest
            {
                RestaurantId = restaurantId.ToString(),
                Query = new ReportPeriodRequest
                {
                    RestaurantId = restaurantId.ToString(),
                    From = query.From ?? string.Empty,
                    To = query.To ?? string.Empty,
                    GroupBy = query.GroupBy,
                    Channel = filters.Channel,
                    MethodCode = filters.MethodCode,
                    Shift = filters.Shift
                }
            };
            if (itemId.HasValue)
            {
                request.ItemId = itemId.Value.ToString();
            }

            var reply = await client.GetDishesAsync(request, options);
            return new RestaurantDishReportDTO(Map(reply.Period), reply.Currency, new SalesReportFilterDTO
            {
                Channel = reply.Filters.Channel,
                MethodCode = reply.Filters.MethodCode,
                Shift = reply.Filters.Shift
            }, reply.HasItemId ? Guid.Parse(reply.ItemId) : null, reply.QuantityComplete,
                reply.Items.Select(item => new DishReportItemDTO(Guid.Parse(item.ItemId), item.Names.ToArray(),
                    new ReportQuantityDTO(decimal.Parse(item.KnownQuantity, CultureInfo.InvariantCulture),
                        item.HasQuantity ? decimal.Parse(item.Quantity, CultureInfo.InvariantCulture) : null),
                    Map(item.GrossSales), Map(item.NetSales), Map(item.Cost), Map(item.GrossProfit),
                    item.Prices.Select(price => new DishPriceSnapshotDTO(decimal.Parse(price.UnitPrice, CultureInfo.InvariantCulture),
                        price.Options.Select(option => new BillOptionSnapshot
                        {
                            OptionId = Guid.Parse(option.OptionId),
                            Name = option.Name,
                            Quantity = option.Quantity,
                            ExtraPrice = decimal.Parse(option.ExtraPrice, CultureInfo.InvariantCulture)
                        }).ToArray())).ToArray(), Map(item.Discounts), item.HasContributionMarginPercent
                        ? decimal.Parse(item.ContributionMarginPercent, CultureInfo.InvariantCulture) : null)).ToArray(),
                reply.Warnings.Select(value => new ReportWarningDTO(value.Code, value.Message)).ToArray());
        }, ct);
    }

    [HttpGet("sales")]
    [EndpointDescription("Báo cáo theo kỳ [from,to). Encode dấu + thành %2B hoặc dùng UTC Z/z. knownAmount là phần đã có bằng chứng; amount=null và isComplete=false khi thiếu nguồn, không phải 0. channel: all/DineIn/Takeaway/Mixed/Unclassified; bộ lọc kênh lấy bill thuần nhóm, không chia tiền bill Mixed; methodCode: all/Cash/BankTransfer; shift: all/unassigned/UUID ca gắn giao dịch. Chưa có lọc món.")]
    [ProducesResponseType(typeof(ResponseDTO<RestaurantSalesReportDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDTO), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetSales(Guid restaurantId, [FromQuery] ReportPeriodQueryDTO query, [FromQuery] SalesReportFilterDTO filters, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var reply = await client.GetSalesAsync(new ReportPeriodRequest
            {
                Channel = filters.Channel,
                MethodCode = filters.MethodCode,
                Shift = filters.Shift,
                RestaurantId = restaurantId.ToString(),
                From = query.From ?? string.Empty,
                To = query.To ?? string.Empty,
                GroupBy = query.GroupBy
            }, options);
            return new RestaurantSalesReportDTO(
                Map(reply.Period),
                reply.Currency,
                Map(reply.Totals),
                reply.Groups.Select(value => new SalesReportGroupDTO(value.Key, Map(value.Totals))).ToArray(),
                reply.Warnings.Select(value => new ReportWarningDTO(value.Code, value.Message)).ToArray(),
                reply.Channels.Select(MapGroup).ToArray(),
                reply.Methods.Select(MapGroup).ToArray(),
                reply.Shifts.Select(MapGroup).ToArray(),
                new SalesReportFilterDTO
                {
                    Channel = reply.Filters.Channel,
                    MethodCode = reply.Filters.MethodCode,
                    Shift = reply.Filters.Shift
                });
        }, ct);
    }

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
            return Map(reply);
        }, ct);
    }

    private static SalesReportGroupDTO MapGroup(SalesReportGroupInfo group)
    {
        return new SalesReportGroupDTO(group.Key, Map(group.Totals));
    }

    private static ReportPeriodDTO Map(ReportPeriodReply reply)
    {
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
    }

    private static SalesReportTotalsDTO Map(SalesReportTotalsInfo source)
    {
        return new SalesReportTotalsDTO(
            Map(source.Collections), Map(source.Refunds), Map(source.NetCollections),
            Map(source.DishRevenue), Map(source.NetDishRevenue), Map(source.Discounts),
            Map(source.Vat), Map(source.ServiceFees),
            new ReportCountDTO(source.PaidOrders.KnownCount,
                source.PaidOrders.HasCount ? source.PaidOrders.Count : null), Map(source.RoundingAdjustment));
    }

    private static ReportAmountDTO Map(ReportAmountInfo source)
    {
        return new ReportAmountDTO(decimal.Parse(source.KnownAmount, CultureInfo.InvariantCulture),
            source.HasAmount ? decimal.Parse(source.Amount, CultureInfo.InvariantCulture) : null);
    }
}
