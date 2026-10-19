using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class CategoryGrpcService(
    ICategoryService service) : CategoryGrpc.CategoryGrpcBase
{
    public override async Task<CategoryReply> Create(
        CreateCategoryRequest request,
        ServerCallContext context)
    {
        var response = await service.Create(
            new CreateCategoryDTO
            {
                CategoryName = request.CategoryName
            },
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<CategoryListReply> GetAll(
        CategoryListRequest request,
        ServerCallContext context)
    {
        var response = await service.GetAll(
            new CategoryQueryDTO
            {
                Page = request.Page,
                PageSize = request.PageSize,
                Search = request.HasSearch ? request.Search : null,
                IsActive = request.HasIsActive ? request.IsActive : null
            },
            context.CancellationToken);

        var reply = new CategoryListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };

        if (response.Result is PagedResult<CategoryDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<CategoryReply> GetById(
        CategoryIdRequest request,
        ServerCallContext context)
    {
        var response = await service.GetById(
            ParseId(request.CategoryId),
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<CategoryReply> Update(
        UpdateCategoryRequest request,
        ServerCallContext context)
    {
        var response = await service.Update(
            ParseId(request.CategoryId),
            new UpdateCategoryDTO
            {
                CategoryName = request.CategoryName
            },
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<CategoryReply> SetStatus(
        SetCategoryStatusRequest request,
        ServerCallContext context)
    {
        var response = await service.SetStatus(
            ParseId(request.CategoryId),
            new SetCategoryStatusDTO
            {
                IsActive = request.HasIsActive ? request.IsActive : null
            },
            context.CancellationToken);

        return ToReply(response);
    }

    public override async Task<CategoryReply> Delete(
        CategoryIdRequest request,
        ServerCallContext context)
    {
        var response = await service.Delete(
            ParseId(request.CategoryId),
            context.CancellationToken);

        return ToReply(response);
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Invalid category ID."));
        }

        return id;
    }

    private static CategoryReply ToReply(ResponseDTO response)
    {
        return new CategoryReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is CategoryDTO category
                ? Map(category)
                : null
        };
    }

    private static CategoryInfo Map(CategoryDTO category)
    {
        return new CategoryInfo
        {
            CategoryId = category.CategoryId.ToString(),
            CategoryCode = category.CategoryCode,
            CategoryName = category.CategoryName,
            IsActive = category.IsActive
        };
    }
}
