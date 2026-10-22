using Backend.Contracts.Inventory;
using Grpc.Core;
using Inventory.BLL.Services;
using Inventory.Common.DTOs;
using Inventory.DAL.Models;

namespace Inventory.API.GrpcServices;

public sealed class StockIssueGrpcService(StockIssueService service) : StockIssueGrpc.StockIssueGrpcBase
{
    public override async Task<StockIssueInfo> Create(StockIssueWriteRequest request, ServerCallContext context)
    {
        return Map(await service.Create(new StockIssueWriteDTO
        {
            StockIssueId = InventoryMapping.Id(request.Id),
            Reason = request.Reason,
            Items = request.Items.Select(line => new StockIssueLineDTO
            {
                IngredientId = InventoryMapping.Id(line.IngredientId),
                Quantity = InventoryMapping.Decimal(line.Quantity)
            }).ToList()
        }, context.CancellationToken));
    }

    public override async Task<StockIssueInfo> GetById(InventoryIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(InventoryMapping.Id(request.Id), context.CancellationToken));
    }

    private static StockIssueInfo Map(StockIssue issue)
    {
        var reply = new StockIssueInfo
        {
            Id = issue.StockIssueId.ToString(),
            CreatedBy = issue.CreatedByRef.ToString(),
            CreatedAt = DateTime.SpecifyKind(issue.CreatedAt, DateTimeKind.Utc).ToString("O"),
            Reason = issue.Reason
        };
        reply.Allocations.AddRange(issue.Movements.OrderBy(x => x.IngredientId).ThenBy(x => x.GoodsReceiptItemId).Select(InventoryMapping.Map));
        return reply;
    }
}
