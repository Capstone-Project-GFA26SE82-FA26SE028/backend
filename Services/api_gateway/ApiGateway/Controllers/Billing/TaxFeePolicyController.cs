using System.Globalization;
using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/tax-fee-policies")]
[RequireBearerToken]
public sealed class TaxFeePolicyController(TaxFeePolicyGrpc.TaxFeePolicyGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDTO<TaxFeePolicyDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Create(CreateTaxFeePolicyDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new CreateTaxFeePolicyRequest();
            if (body.VatRate.HasValue)
            {
                request.VatRate = body.VatRate.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.ServiceRate.HasValue)
            {
                request.ServiceRate = body.ServiceRate.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (body.ServiceEnabled.HasValue)
            {
                request.ServiceEnabled = body.ServiceEnabled.Value;
            }

            if (body.EffectiveFrom is not null)
            {
                request.EffectiveFrom = body.EffectiveFrom;
            }

            return Map(await client.CreateAsync(request, options));
        }, ct, 201);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<TaxFeePolicyDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] TaxFeePolicyQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var source = await client.GetAllAsync(new TaxFeePolicyListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            }, options);
            return new PagedResult<TaxFeePolicyDTO>(source.Items.Select(Map).ToArray(), source.TotalCount, source.Page, source.PageSize);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<TaxFeePolicyDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new TaxFeePolicyIdRequest
        {
            TaxFeePolicyId = id.ToString()
        }, options)), ct);
    }

    private static TaxFeePolicyDTO Map(TaxFeePolicyInfo source)
    {
        return new TaxFeePolicyDTO
        {
            TaxFeePolicyId = Guid.Parse(source.TaxFeePolicyId),
            Version = source.Version,
            VatRate = decimal.Parse(source.VatRate, CultureInfo.InvariantCulture),
            ServiceRate = decimal.Parse(source.ServiceRate, CultureInfo.InvariantCulture),
            ServiceEnabled = source.ServiceEnabled,
            EffectiveFrom = DateTimeOffset.Parse(source.EffectiveFrom, CultureInfo.InvariantCulture)
        };
    }
}
