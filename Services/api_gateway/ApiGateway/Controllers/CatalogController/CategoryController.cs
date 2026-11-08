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
[Route("api/categories")]
[RequireBearerToken]
public sealed class CategoryController(
    CategoryGrpc.CategoryGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(CreateCategoryDTO body, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAsync(new CreateCategoryRequest
            {
                CategoryName = body.CategoryName ?? string.Empty
            }, Options(cancellationToken));

            if (reply.IsSuccess && reply.StatusCode == 201 && reply.Result is not null)
            {
                Response.Headers.Location =
                    $"/api/categories/{Uri.EscapeDataString(reply.Result.CategoryId)}";
            }

            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(
        [FromQuery] CategoryQueryDTO query,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new CategoryListRequest
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            if (query.IsActive.HasValue)
            {
                request.IsActive = query.IsActive.Value;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<CategoryDTO>(
                        reply.Items.Select(Map).ToArray(),
                        reply.TotalCount,
                        reply.Page,
                        reply.PageSize)
                    : null
            });
        }, cancellationToken);
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new CategoryIdRequest
            {
                CategoryId = id.ToString()
            }, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpPut("{id}")]
    public Task<IActionResult> Update(
        Guid id,
        UpdateCategoryDTO body,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.UpdateAsync(new UpdateCategoryRequest
            {
                CategoryId = id.ToString(),
                CategoryName = body.CategoryName ?? string.Empty
            }, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpPatch("{id}/status")]
    public Task<IActionResult> SetStatus(
        Guid id,
        SetCategoryStatusDTO body,
        CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new SetCategoryStatusRequest
            {
                CategoryId = id.ToString()
            };
            if (body.IsActive.HasValue)
            {
                request.IsActive = body.IsActive.Value;
            }

            var reply = await client.SetStatusAsync(request, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var reply = await client.DeleteAsync(new CategoryIdRequest
            {
                CategoryId = id.ToString()
            }, Options(cancellationToken));
            return ToResponse(reply);
        }, cancellationToken);
    }

    private IActionResult ToResponse(CategoryReply reply)
    {
        return StatusCode(reply.StatusCode, new ResponseDTO
        {
            StatusCode = reply.StatusCode,
            Message = reply.Message,
            IsSuccess = reply.IsSuccess,
            Result = reply.Result is null ? null : Map(reply.Result)
        });
    }

    private static CategoryDTO Map(CategoryInfo category)
    {
        return new CategoryDTO
        {
            CategoryId = Guid.Parse(category.CategoryId),
            CategoryCode = category.CategoryCode,
            CategoryName = category.CategoryName,
            IsActive = category.IsActive
        };
    }
}
