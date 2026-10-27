using System.Globalization;
using System.Text.Json;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class BillGrpcService(IBillService service) : BillGrpc.BillGrpcBase
{
    public override async Task<BillInfo> Create(CreateBillRequest request, ServerCallContext context)
    {
        return Map(await service.Create(Id(request.TableSessionId), context.CancellationToken));
    }

    public override async Task<BillInfo> GetById(BillIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.BillId), context.CancellationToken));
    }

    public override async Task<BillPage> GetAll(BillListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(new BillQueryDTO
        {
            RestaurantId = request.HasRestaurantId ? Id(request.RestaurantId) : null,
            Search = request.HasSearch ? request.Search : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);
        var page = new BillPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        page.Items.AddRange(result.Items.Select(Map));
        return page;
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid ID is required."));
        }

        return id;
    }

    private static string Decimal(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static BillInfo Map(BillDTO source)
    {
        var result = new BillInfo
        {
            BillId = source.BillId.ToString(),
            RestaurantId = source.RestaurantId.ToString(),
            BillNumber = source.BillNumber,
            Status = source.Status,
            Currency = source.Currency,
            SubTotal = Decimal(source.SubTotal),
            DiscountAmount = Decimal(source.DiscountAmount),
            VatRateSnapshot = Decimal(source.VatRateSnapshot),
            ServiceRateSnapshot = Decimal(source.ServiceRateSnapshot),
            VatAmount = Decimal(source.VatAmount),
            ServiceAmount = Decimal(source.ServiceAmount),
            TotalAmount = Decimal(source.TotalAmount),
            RoundingAdjustment = Decimal(source.RoundingAdjustment),
            Version = source.Version,
            HasCompleteCogs = source.HasCompleteCogs,
            SourceStatus = source.SourceStatus
        };
        result.Warnings.AddRange(source.Warnings);
        result.Items.AddRange(source.Items.Select(line =>
        {
            var item = new BillLineInfo
            {
                BillLineId = line.BillLineId.ToString(),
                OrderItemRef = line.OrderItemRef.ToString(),
                TableSessionRef = line.TableSessionRef.ToString(),
                OriginalTableRef = line.OriginalTableRef.ToString(),
                ItemSnapshotJson = JsonSerializer.Serialize(line.Item),
                Quantity = Decimal(line.Quantity),
                UnitPriceSnapshot = Decimal(line.UnitPriceSnapshot),
                LineTotal = Decimal(line.LineTotal)
            };
            if (line.ComboGroupRef.HasValue)
            {
                item.ComboGroupRef = line.ComboGroupRef.Value.ToString();
            }

            if (line.CogsSnapshot.HasValue)
            {
                item.CogsSnapshot = Decimal(line.CogsSnapshot.Value);
            }

            return item;
        }));
        return result;
    }
}
