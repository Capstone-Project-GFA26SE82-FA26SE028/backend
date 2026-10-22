using System.Globalization;
using ApiGateway.DTOs.Inventory;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/ingredients")]
[RequireBearerToken]
public sealed class IngredientController(IngredientGrpc.IngredientGrpcClient client) : InventoryControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(IngredientWriteDTO body, CancellationToken ct)
    {
        return Execute(async () => InventoryMapping.Map(await client.CreateAsync(Write(body), Options(ct))), ct, 201);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, IngredientWriteDTO body, CancellationToken ct)
    {
        var request = Write(body);
        request.Id = id.ToString();
        return Execute(async () => InventoryMapping.Map(await client.UpdateAsync(request, Options(ct))), ct);
    }

    [HttpPatch("{id:guid}/status")]
    public Task<IActionResult> SetStatus(Guid id, IngredientStatusDTO body, CancellationToken ct)
    {
        return Execute(async () => InventoryMapping.Map(await client.SetStatusAsync(new IngredientStatusRequest
        {
            Id = id.ToString(),
            IsActive = body.IsActive!.Value
        }, Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => InventoryMapping.Map(await client.GetByIdAsync(new InventoryIdRequest
        {
            Id = id.ToString()
        }, Options(ct))), ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20,
        string? search = null, bool? isActive = null, bool lowStockOnly = false)
    {
        return Execute(async () =>
        {
            var request = new IngredientListRequest
            {
                Page = page,
                PageSize = pageSize,
                LowStockOnly = lowStockOnly
            };
            if (search is not null)
            {
                request.Search = search;
            }
            if (isActive.HasValue)
            {
                request.IsActive = isActive.Value;
            }
            var reply = await client.GetAllAsync(request, Options(ct));
            return new
            {
                Items = reply.Items.Select(InventoryMapping.Map).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            };
        }, ct);
    }

    private static IngredientWriteRequest Write(IngredientWriteDTO body)
    {
        return new IngredientWriteRequest
        {
            Name = body.IngredientName,
            BaseUnit = body.BaseUnit,
            LowStockThreshold = body.LowStockThreshold.ToString(CultureInfo.InvariantCulture)
        };
    }
}
