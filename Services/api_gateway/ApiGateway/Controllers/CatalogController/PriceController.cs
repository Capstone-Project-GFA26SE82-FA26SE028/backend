using System.Globalization;
using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/prices")]
[RequireBearerToken]
public sealed class PriceController(
    PriceGrpc.PriceGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(CreatePriceDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CreatePriceRequest
            {
                ItemId = body.ItemId.ToString(),
                Channel = body.Channel ?? string.Empty,
                Amount = body.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                EffectiveFrom = body.EffectiveFrom ?? string.Empty
            };
            if (body.EffectiveTo is not null)
            {
                request.EffectiveTo = body.EffectiveTo;
            }

            if (body.DailyStart is not null)
            {
                request.DailyStart = body.DailyStart;
            }

            if (body.DailyEnd is not null)
            {
                request.DailyEnd = body.DailyEnd;
            }

            return ToResponse(await client.CreateAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] PriceQueryDTO query, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new PriceListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.ItemId.HasValue)
            {
                request.ItemId = query.ItemId.Value.ToString();
            }

            if (query.Channel is not null)
            {
                request.Channel = query.Channel;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<PriceDTO>(reply.Items.Select(Map).ToArray(),
                        reply.TotalCount, reply.Page, reply.PageSize)
                    : null
            });
        }, cancellationToken);
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () => ToResponse(await client.GetByIdAsync(new PriceIdRequest
        {
            PriceId = id.ToString()
        }, Options(cancellationToken))), cancellationToken);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(
        Guid id, UpdatePriceDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new UpdatePriceRequest
            {
                PriceId = id.ToString(),
                Channel = body.Channel ?? string.Empty,
                Amount = body.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                EffectiveFrom = body.EffectiveFrom ?? string.Empty
            };
            if (body.EffectiveTo is not null)
            {
                request.EffectiveTo = body.EffectiveTo;
            }

            if (body.DailyStart is not null)
            {
                request.DailyStart = body.DailyStart;
            }

            if (body.DailyEnd is not null)
            {
                request.DailyEnd = body.DailyEnd;
            }

            return ToResponse(await client.UpdateAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.DeleteAsync(new DeletePriceRequest
            {
                PriceId = id.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPost("{id}/replace")]
    public Task<IActionResult> Replace(
        Guid id, ReplacePriceDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new ReplacePriceRequest
            {
                PriceId = id.ToString(),
                Amount = body.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            };
            if (body.EffectiveTo is not null)
            {
                request.EffectiveTo = body.EffectiveTo;
            }

            return ToResponse(await client.ReplaceAsync(request, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpGet("/api/items/{itemId}/price")]
    public Task<IActionResult> GetAppliedPrice(
        Guid itemId, [FromQuery] string? channel, [FromQuery] string? at, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new AppliedPriceRequest
            {
                ItemId = itemId.ToString(),
                Channel = channel ?? string.Empty
            };
            if (at is not null)
            {
                request.At = at;
            }

            var reply = await client.GetAppliedPriceAsync(request, Options(cancellationToken));
            AppliedPriceDTO? result = null;
            if (reply.Result is not null)
            {
                var price = reply.Result;
                result = new AppliedPriceDTO
                {
                    ItemId = Guid.Parse(price.ItemId),
                    PriceId = Guid.Parse(price.PriceId),
                    Amount = decimal.Parse(price.Amount, CultureInfo.InvariantCulture),
                    RequestedChannel = price.RequestedChannel,
                    PriceChannel = price.PriceChannel,
                    EvaluatedAt = DateTimeOffset.Parse(price.EvaluatedAt, CultureInfo.InvariantCulture),
                    HasValidStandardPrice = price.HasValidStandardPrice,
                    Price = Map(price.Price)
                };
            }

            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = result
            });
        }, cancellationToken);
    }

    private IActionResult ToResponse(PriceReply reply)
    {
        var price = reply.Result is null ? null : Map(reply.Result);
        if (reply.IsSuccess && price is not null)
        {
            if (reply.StatusCode == 201)
            {
                Response.Headers.Location = $"/api/prices/{price.PriceId}";
            }
        }

        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = price
        });
    }

    private static PriceDTO Map(PriceInfo price)
    {
        return new PriceDTO
        {
            PriceId = Guid.Parse(price.PriceId),
            ItemId = Guid.Parse(price.ItemId),
            Channel = price.Channel,
            Amount = decimal.Parse(price.Amount, CultureInfo.InvariantCulture),
            EffectiveFrom = DateTimeOffset.Parse(price.EffectiveFrom, CultureInfo.InvariantCulture),
            EffectiveTo = price.HasEffectiveTo ? DateTimeOffset.Parse(price.EffectiveTo, CultureInfo.InvariantCulture) : null,
            DailyStart = price.HasDailyStart ? TimeOnly.ParseExact(price.DailyStart, "HH:mm:ss", CultureInfo.InvariantCulture) : null,
            DailyEnd = price.HasDailyEnd ? TimeOnly.ParseExact(price.DailyEnd, "HH:mm:ss", CultureInfo.InvariantCulture) : null
        };
    }
}
