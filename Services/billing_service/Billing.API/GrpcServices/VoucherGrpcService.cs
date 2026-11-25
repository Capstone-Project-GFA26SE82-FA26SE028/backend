using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class VoucherGrpcService(IVoucherService service) : VoucherGrpc.VoucherGrpcBase
{
    public override async Task<VoucherInfo> Create(CreateVoucherRequest request, ServerCallContext context)
    {
        var dto = new CreateVoucherDTO
        {
            VoucherCode = request.VoucherCode,
            DiscountType = request.DiscountType,
            DiscountValue = ParseDecimal(request.DiscountValue),
            MaximumDiscount = request.HasMaximumDiscount ? ParseDecimal(request.MaximumDiscount) : null,
            UsageLimit = request.HasUsageLimit ? request.UsageLimit : null,
            StartAt = DateTime.Parse(request.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            EndAt = request.HasEndAt ? DateTime.Parse(request.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = request.IsActive
        };

        var result = await service.Create(dto, context.CancellationToken);
        return Map(result);
    }

    public override async Task<VoucherInfo> Update(UpdateVoucherRequest request, ServerCallContext context)
    {
        var dto = new UpdateVoucherDTO
        {
            DiscountType = request.HasDiscountType ? request.DiscountType : null,
            DiscountValue = request.HasDiscountValue ? ParseDecimal(request.DiscountValue) : null,
            MaximumDiscount = request.HasMaximumDiscount ? ParseDecimal(request.MaximumDiscount) : null,
            UsageLimit = request.HasUsageLimit ? request.UsageLimit : null,
            StartAt = request.HasStartAt ? DateTime.Parse(request.StartAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            EndAt = request.HasEndAt ? DateTime.Parse(request.EndAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : null,
            IsActive = request.HasIsActive ? request.IsActive : null
        };

        var result = await service.Update(Guid.Parse(request.VoucherId), dto, context.CancellationToken);
        return Map(result);
    }

    public override async Task<VoucherPage> GetAll(VoucherListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(new VoucherQueryDTO
        {
            Search = request.HasSearch ? request.Search : null,
            IsActive = request.HasIsActive ? request.IsActive : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);

        var page = new VoucherPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        page.Items.AddRange(result.Items.Select(Map));
        return page;
    }

    public override async Task<VoucherInfo> GetById(VoucherIdRequest request, ServerCallContext context)
    {
        var result = await service.GetById(Guid.Parse(request.VoucherId), context.CancellationToken);
        return Map(result);
    }

    public override async Task<VoucherInfo> SetStatus(SetVoucherStatusRequest request, ServerCallContext context)
    {
        var result = await service.SetStatus(Guid.Parse(request.VoucherId), request.IsActive, context.CancellationToken);
        return Map(result);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string Decimal(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static VoucherInfo Map(VoucherDTO dto)
    {
        var info = new VoucherInfo
        {
            VoucherId = dto.VoucherId.ToString(),
            NormalizedVoucherCode = dto.NormalizedVoucherCode,
            DiscountType = dto.DiscountType,
            DiscountValue = Decimal(dto.DiscountValue),
            CurrentUsage = dto.CurrentUsage,
            StartAt = dto.StartAt.ToString("o"),
            IsActive = dto.IsActive
        };

        if (dto.MaximumDiscount.HasValue)
        {
            info.MaximumDiscount = Decimal(dto.MaximumDiscount.Value);
        }

        if (dto.UsageLimit.HasValue)
        {
            info.UsageLimit = dto.UsageLimit.Value;
        }

        if (dto.EndAt.HasValue)
        {
            info.EndAt = dto.EndAt.Value.ToString("o");
        }

        return info;
    }
}
