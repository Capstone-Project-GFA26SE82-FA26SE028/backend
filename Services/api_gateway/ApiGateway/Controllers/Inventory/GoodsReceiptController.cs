using System.Globalization;
using ApiGateway.DTOs.Inventory;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/goods-receipts")]
[RequireBearerToken]
public sealed class GoodsReceiptController(GoodsReceiptGrpc.GoodsReceiptGrpcClient client) : InventoryControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(ReceiptWriteDTO body, CancellationToken ct)
    {
        if (body.Items is null || body.Items.Any(x => x is null || x.ExpiryDate is null))
        {
            return Task.FromResult<IActionResult>(BadRequest(new ResponseDTO("Receipt lines and expiry dates are required.", 400)));
        }
        var request = new ReceiptWriteRequest
        {
            Id = body.GoodsReceiptId.ToString(),
            Note = body.Note ?? ""
        };
        request.Items.AddRange(body.Items.Select(line => new ReceiptLine
        {
            UnitId = line.UnitId?.ToString() ?? "",
            IngredientId = line.IngredientId.ToString(),
            ReceivedQuantity = line.ReceivedQuantity.ToString(CultureInfo.InvariantCulture),
            ExpiryDate = line.ExpiryDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }));
        return Execute(async () => InventoryMapping.Map(await client.CreateAsync(request, Options(ct))), ct);
    }

    [HttpPost("{id:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        return Execute(async () => InventoryMapping.Map(await client.ConfirmAsync(new InventoryIdRequest
        {
            Id = id.ToString()
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
    public Task<IActionResult> GetAll(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(new InventoryPageRequest
            {
                Page = page,
                PageSize = pageSize
            }, Options(ct));
            return new
            {
                Items = reply.Items.Select(InventoryMapping.Map).ToList(),
                reply.TotalCount,
                reply.Page,
                reply.PageSize
            };
        }, ct);
    }
}
