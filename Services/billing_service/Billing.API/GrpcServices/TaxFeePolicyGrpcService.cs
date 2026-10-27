using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class TaxFeePolicyGrpcService(ITaxFeePolicyService service) : TaxFeePolicyGrpc.TaxFeePolicyGrpcBase
{
    public override async Task<TaxFeePolicyInfo> Create(CreateTaxFeePolicyRequest request, ServerCallContext context)
    {
        var result = await service.Create(new CreateTaxFeePolicyDTO
        {
            VatRate = request.HasVatRate ? Rate(request.VatRate) : null,
            ServiceRate = request.HasServiceRate ? Rate(request.ServiceRate) : null,
            ServiceEnabled = request.HasServiceEnabled ? request.ServiceEnabled : null,
            EffectiveFrom = request.HasEffectiveFrom ? request.EffectiveFrom : null
        }, context.CancellationToken);
        return Map(result);
    }

    public override async Task<TaxFeePolicyInfo> GetById(TaxFeePolicyIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TaxFeePolicyId, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "A valid tax policy ID is required.");
        }

        return Map(await service.GetById(id, context.CancellationToken));
    }

    public override async Task<TaxFeePolicyPage> GetAll(TaxFeePolicyListRequest request, ServerCallContext context)
    {
        var source = await service.GetAll(new TaxFeePolicyQueryDTO
        {
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);
        var result = new TaxFeePolicyPage
        {
            TotalCount = source.TotalCount,
            Page = source.Page,
            PageSize = source.PageSize
        };
        result.Items.AddRange(source.Items.Select(Map));
        return result;
    }

    private static decimal Rate(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var rate))
        {
            throw new BillingException(400, "Provide rates as invariant decimal fractions, for example 0.08.");
        }

        return rate;
    }

    private static TaxFeePolicyInfo Map(TaxFeePolicyDTO source)
    {
        return new TaxFeePolicyInfo
        {
            TaxFeePolicyId = source.TaxFeePolicyId.ToString(),
            Version = source.Version,
            VatRate = source.VatRate.ToString(CultureInfo.InvariantCulture),
            ServiceRate = source.ServiceRate.ToString(CultureInfo.InvariantCulture),
            ServiceEnabled = source.ServiceEnabled,
            EffectiveFrom = source.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture)
        };
    }
}
