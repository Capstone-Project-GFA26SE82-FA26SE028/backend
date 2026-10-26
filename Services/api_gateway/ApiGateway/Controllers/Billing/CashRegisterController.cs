using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Grpc.Core;
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
    IOptions<BillingClientSettings> settings) : ControllerBase
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

    private async Task<IActionResult> Execute(Func<CallOptions, Task<object>> action, CancellationToken ct, int status = 200)
    {
        Response.Headers.CacheControl = "no-store";
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized(new ResponseDTO("A valid employee session is required.", 401));
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var options = new CallOptions(headers, DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct);
        try
        {
            var result = await action(options);
            return StatusCode(status, new ResponseDTO("Success.", status, true, result));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var code = error.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.Aborted or Grpc.Core.StatusCode.FailedPrecondition => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            var message = code is 502 or 503 or 504
                ? "Billing or a required service is unavailable."
                : error.Status.Detail;
            return StatusCode(code, new ResponseDTO(message, code));
        }
    }

    private static CashRegisterDTO Map(CashRegisterInfo value)
    {
        return new CashRegisterDTO(Guid.Parse(value.CashRegisterId), Guid.Parse(value.RestaurantId), value.Code);
    }
}
