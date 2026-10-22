using System.Globalization;
using ApiGateway.DTOs.Inventory;
using ApiGateway.Swagger;
using Backend.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Inventory;

[ApiController]
[Route("api/stock-issues")]
[RequireBearerToken]
public sealed class StockIssueController(StockIssueGrpc.StockIssueGrpcClient client) : InventoryControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(StockIssueWriteDTO body, CancellationToken ct)
    {
        if (body.Items is null || body.Items.Any(x => x is null))
        {
            return Task.FromResult<IActionResult>(BadRequest(new ResponseDTO("Issue lines are required.", 400)));
        }
        var request = new StockIssueWriteRequest
        {
            Id = body.StockIssueId.ToString(),
            Reason = body.Reason
        };
        request.Items.AddRange(body.Items.Select(line => new StockIssueLine
        {
            IngredientId = line.IngredientId.ToString(),
            Quantity = line.Quantity.ToString(CultureInfo.InvariantCulture)
        }));
        return Execute(async () => Map(await client.CreateAsync(request, Options(ct))), ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () => Map(await client.GetByIdAsync(new InventoryIdRequest
        {
            Id = id.ToString()
        }, Options(ct))), ct);
    }

    private static object Map(StockIssueInfo issue)
    {
        return new
        {
            StockIssueId = issue.Id,
            CreatedByRef = issue.CreatedBy,
            issue.CreatedAt,
            issue.Reason,
            Allocations = issue.Allocations.Select(InventoryMapping.Map).ToList()
        };
    }
}
