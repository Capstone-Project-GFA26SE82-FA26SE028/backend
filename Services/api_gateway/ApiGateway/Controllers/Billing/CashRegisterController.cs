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
[Route("api/cash-registers")]
[RequireBearerToken]
public sealed class CashRegisterController(
    CashRegisterGrpc.CashRegisterGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDTO<CashRegisterDTO>), StatusCodes.Status201Created)]
    public Task<IActionResult> Create(CreateCashRegisterDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var reply = await client.CreateAsync(new CreateCashRegisterRequest
            {
                RestaurantId = body.RestaurantId.ToString(),
                Code = body.Code ?? string.Empty
            }, options);
            return Map(reply);
        }, ct, 201);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ResponseDTO<PagedResult<CashRegisterDTO>>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAll([FromQuery] CashRegisterQueryDTO query, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var request = new CashRegisterListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.RestaurantId.HasValue)
            {
                request.RestaurantId = query.RestaurantId.Value.ToString();
            }

            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            var reply = await client.GetAllAsync(request, options);
            return new PagedResult<CashRegisterDTO>
            {
                Items = reply.Items.Select(Map).ToArray(),
                TotalCount = reply.TotalCount,
                Page = reply.Page,
                PageSize = reply.PageSize
            };
        }, ct);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDTO<CashRegisterDTO>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new CashRegisterIdRequest
        {
            CashRegisterId = id.ToString()
        }, options)), ct);
    }

    private static CashRegisterDTO Map(CashRegisterInfo value)
    {
        return new CashRegisterDTO(Guid.Parse(value.CashRegisterId), Guid.Parse(value.RestaurantId), value.Code);
    }
}
