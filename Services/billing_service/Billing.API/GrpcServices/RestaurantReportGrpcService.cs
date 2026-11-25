using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class RestaurantReportGrpcService(IRestaurantReportService service, IRestaurantSalesReportService sales, IRestaurantDishReportService dishes) : RestaurantReportGrpc.RestaurantReportGrpcBase
{
    public override async Task<DishReportReply> GetDishes(DishReportRequest request, ServerCallContext context)
    {
        var query = request.Query;
        if (query is null || !Guid.TryParse(request.RestaurantId, out var owner)
            || !Guid.TryParse(query.RestaurantId, out var restaurantId) || owner != restaurantId
            || (request.HasItemId && !Guid.TryParse(request.ItemId, out _)))
        {
            throw new BillingException(400, "Mã nhà hàng hoặc món lọc không hợp lệ.");
        }

        var result = await dishes.GetDishes(restaurantId, new ReportPeriodQueryDTO
        {
            From = query.From,
            To = query.To,
            GroupBy = query.GroupBy
        }, new SalesReportFilterDTO
        {
            Channel = query.Channel.Length == 0 ? "all" : query.Channel,
            MethodCode = query.MethodCode.Length == 0 ? "all" : query.MethodCode,
            Shift = query.Shift.Length == 0 ? "all" : query.Shift
        }, request.HasItemId ? Guid.Parse(request.ItemId) : null, context.CancellationToken);
        var reply = new DishReportReply
        {
            Currency = result.Currency,
            Period = Map(result.Period),
            Filters = new SalesReportFiltersInfo
            {
                Channel = result.Filters.Channel,
                MethodCode = result.Filters.MethodCode,
                Shift = result.Filters.Shift
            },
            QuantityComplete = result.QuantityComplete
        };
        if (result.ItemId.HasValue)
        {
            reply.ItemId = result.ItemId.Value.ToString();
        }

        reply.Items.AddRange(result.Items.Select(item =>
        {
            var mapped = new DishReportItemInfo
            {
                ItemId = item.ItemId.ToString(),
                KnownQuantity = item.Quantity.KnownQuantity.ToString(CultureInfo.InvariantCulture),
                GrossSales = Map(item.GrossSales),
                NetSales = Map(item.NetSales),
                Cost = Map(item.Cost),
                GrossProfit = Map(item.GrossProfit),
                Discounts = Map(item.Discounts)
            };
            if (item.Quantity.Quantity.HasValue)
            {
                mapped.Quantity = item.Quantity.Quantity.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (item.ContributionMarginPercent.HasValue)
            {
                mapped.ContributionMarginPercent = item.ContributionMarginPercent.Value.ToString(CultureInfo.InvariantCulture);
            }

            mapped.Names.AddRange(item.Names);
            mapped.Prices.AddRange(item.Prices.Select(price =>
            {
                var value = new DishReportPriceInfo
                {
                    UnitPrice = price.UnitPrice.ToString(CultureInfo.InvariantCulture)
                };
                value.Options.AddRange(price.Options.Select(option => new DishReportOptionInfo
                {
                    OptionId = option.OptionId.ToString(),
                    Name = option.Name,
                    Quantity = option.Quantity,
                    ExtraPrice = option.ExtraPrice.ToString(CultureInfo.InvariantCulture)
                }));
                return value;
            }));
            return mapped;
        }));
        reply.Warnings.AddRange(result.Warnings.Select(value => new ReportWarningInfo
        {
            Code = value.Code,
            Message = value.Message
        }));
        return reply;
    }

    public override async Task<RestaurantSalesReportReply> GetSales(ReportPeriodRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId) || restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã nhà hàng hợp lệ.");
        }

        var result = await sales.GetSales(restaurantId, new ReportPeriodQueryDTO
        {
            From = request.From,
            To = request.To,
            GroupBy = request.GroupBy
        }, context.CancellationToken, new SalesReportFilterDTO
        {
            Channel = request.Channel.Length == 0 ? "all" : request.Channel,
            MethodCode = request.MethodCode.Length == 0 ? "all" : request.MethodCode,
            Shift = request.Shift.Length == 0 ? "all" : request.Shift
        });
        var reply = new RestaurantSalesReportReply
        {
            Period = Map(result.Period),
            Currency = result.Currency,
            Filters = new SalesReportFiltersInfo
            {
                Channel = result.Filters.Channel,
                MethodCode = result.Filters.MethodCode,
                Shift = result.Filters.Shift
            },
            Totals = Map(result.Totals)
        };
        reply.Groups.AddRange(result.Groups.Select(value => new SalesReportGroupInfo
        {
            Key = value.Key,
            Totals = Map(value.Totals)
        }));
        reply.Warnings.AddRange(result.Warnings.Select(value => new ReportWarningInfo
        {
            Code = value.Code,
            Message = value.Message
        }));
        reply.Channels.AddRange(result.Channels.Select(MapGroup));
        reply.Methods.AddRange(result.Methods.Select(MapGroup));
        reply.Shifts.AddRange(result.Shifts.Select(MapGroup));
        return reply;
    }

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
        return Map(result);
    }

    private static SalesReportGroupInfo MapGroup(SalesReportGroupDTO group)
    {
        return new SalesReportGroupInfo
        {
            Key = group.Key,
            Totals = Map(group.Totals)
        };
    }

    private static ReportPeriodReply Map(ReportPeriodDTO result)
    {
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

    private static SalesReportTotalsInfo Map(SalesReportTotalsDTO result)
    {
        var reply = new SalesReportTotalsInfo
        {
            RoundingAdjustment = Map(result.RoundingAdjustment),
            Collections = Map(result.Collections),
            Refunds = Map(result.Refunds),
            NetCollections = Map(result.NetCollections),
            DishRevenue = Map(result.DishRevenue),
            NetDishRevenue = Map(result.NetDishRevenue),
            Discounts = Map(result.Discounts),
            Vat = Map(result.Vat),
            ServiceFees = Map(result.ServiceFees),
            PaidOrders = new ReportCountInfo
            {
                KnownCount = result.PaidOrders.KnownCount
            }
        };
        if (result.PaidOrders.Count.HasValue)
        {
            reply.PaidOrders.Count = result.PaidOrders.Count.Value;
        }

        return reply;
    }

    private static ReportAmountInfo Map(ReportAmountDTO result)
    {
        var reply = new ReportAmountInfo
        {
            KnownAmount = result.KnownAmount.ToString(CultureInfo.InvariantCulture)
        };
        if (result.Amount.HasValue)
        {
            reply.Amount = result.Amount.Value.ToString(CultureInfo.InvariantCulture);
        }

        return reply;
    }
}
