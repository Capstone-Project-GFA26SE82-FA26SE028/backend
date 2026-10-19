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
[Route("api/option-groups")]
[RequireBearerToken]
public sealed class OptionGroupController(
    OptionGroupGrpc.OptionGroupGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(CreateOptionGroupDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.CreateAsync(new CreateOptionGroupRequest
            {
                OptionGroupName = body.OptionGroupName ?? string.Empty
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] OptionGroupQueryDTO query, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new OptionGroupListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<OptionGroupDTO>(reply.Items.Select(Map).ToArray(),
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
            return ToResponse(await client.GetByIdAsync(new OptionGroupIdRequest
            {
                OptionGroupId = id.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(Guid id, UpdateOptionGroupDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.UpdateAsync(new UpdateOptionGroupRequest
            {
                OptionGroupId = id.ToString(),
                OptionGroupName = body.OptionGroupName ?? string.Empty
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            return ToResponse(await client.DeleteAsync(new OptionGroupIdRequest
            {
                OptionGroupId = id.ToString()
            }, Options(cancellationToken)));
        }, cancellationToken);
    }

    private IActionResult ToResponse(OptionGroupReply reply)
    {
        if (reply.IsSuccess && reply.StatusCode == 201 && reply.Result is not null)
        {
            Response.Headers.Location = $"/api/option-groups/{Uri.EscapeDataString(reply.Result.OptionGroupId)}";
        }

        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static OptionGroupDTO Map(OptionGroupInfo entity)
    {
        return new OptionGroupDTO
        {
            OptionGroupId = Guid.Parse(entity.OptionGroupId),
            OptionGroupName = entity.OptionGroupName
        };
    }
}
