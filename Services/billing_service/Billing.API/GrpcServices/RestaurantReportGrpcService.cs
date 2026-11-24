using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class RestaurantReportGrpcService(IRestaurantReportService service) : RestaurantReportGrpc.RestaurantReportGrpcBase
{
    public override async Task<ReportPeriodReply> GetPeriod(ReportPeriodRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã nhà hàng hợp lệ.");
        }

        var result = await service.GetPeriod(restaurantId, new ReportPeriodQueryDTO
        {
            From = request.From,
            To = request.To,
            GroupBy = request.GroupBy
        }, context.CancellationToken);
        return new ReportPeriodReply
        {
            RestaurantId = result.RestaurantId.ToString(),
            TimeZone = result.TimeZone,
            FromUtc = result.FromUtc.ToString("O", CultureInfo.InvariantCulture),
            ToUtc = result.ToUtc.ToString("O", CultureInfo.InvariantCulture),
            FromLocal = result.FromLocal.ToString("O", CultureInfo.InvariantCulture),
            ToLocal = result.ToLocal.ToString("O", CultureInfo.InvariantCulture),
            GroupBy = result.GroupBy,
            WeekStartsOn = result.WeekStartsOn
        };
    }
}
