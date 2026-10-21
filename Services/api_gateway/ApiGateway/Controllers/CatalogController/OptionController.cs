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
[Route("api/options")]
[RequireBearerToken]
public sealed class OptionController(
    OptionGrpc.OptionGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(CreateOptionDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.CreateAsync(new CreateOptionRequest
            {
                OptionGroupId = body.OptionGroupId.ToString(),
                OptionName = body.OptionName ?? string.Empty,
                ExtraPrice = body.ExtraPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] OptionQueryDTO query, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new OptionListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            if (query.OptionGroupId.HasValue)
            {
                request.OptionGroupId = query.OptionGroupId.Value.ToString();
            }

            if (query.Availability is not null)
            {
                request.Availability = query.Availability;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<OptionDTO>(reply.Items.Select(Map).ToArray(),
                        reply.TotalCount, reply.Page, reply.PageSize)
                    : null
            });
        }, cancellationToken);
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.GetByIdAsync(new OptionIdRequest
            {
                OptionId = id.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(Guid id, UpdateOptionDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.UpdateAsync(new UpdateOptionRequest
            {
                OptionId = id.ToString(),
                OptionName = body.OptionName ?? string.Empty,
                ExtraPrice = body.ExtraPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.DeleteAsync(new OptionIdRequest
            {
                OptionId = id.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPatch("{id}/status")]
    public Task<IActionResult> SetStatus(Guid id, SetOptionStatusDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.SetStatusAsync(new SetOptionStatusRequest
            {
                OptionId = id.ToString(),
                Availability = body.Availability ?? string.Empty
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    private IActionResult ToResponse(OptionReply reply)
    {
        if (reply.IsSuccess && reply.StatusCode == 201 && reply.Result is not null)
        {
            Response.Headers.Location = $"/api/options/{Uri.EscapeDataString(reply.Result.OptionId)}";
        }

        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static OptionDTO Map(OptionInfo entity)
    {
        return new OptionDTO
        {
            OptionId = Guid.Parse(entity.OptionId),
            OptionGroupId = Guid.Parse(entity.OptionGroupId),
            OptionName = entity.OptionName,
            ExtraPrice = decimal.Parse(entity.ExtraPrice, CultureInfo.InvariantCulture),
            Availability = entity.Availability
        };
    }
}
