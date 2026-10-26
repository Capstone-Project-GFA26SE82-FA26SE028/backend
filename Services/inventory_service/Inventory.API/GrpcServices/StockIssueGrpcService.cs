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

    public override async Task<CookingDeductionReply> DeductForCooking(CookingDeductionRequest request, ServerCallContext context)
    {
        var dto = new CookingDeductionDTO
        {
            CookingEventId = InventoryMapping.Id(request.CookingEventId),
            Items = request.Items.Select(item => new CookingItemDeductionDTO
            {
                TaskId = InventoryMapping.Id(item.TaskId),
                OrderItemId = InventoryMapping.Id(item.OrderItemId),
                DishName = item.DishName ?? string.Empty,
                Quantity = item.Quantity,
                Ingredients = item.Ingredients.Select(line => new StockIssueLineDTO
                {
                    IngredientId = InventoryMapping.Id(line.IngredientId),
                    Quantity = InventoryMapping.Decimal(line.Quantity)
                }).ToList()
            }).ToList()
        };

        var result = await service.DeductForCooking(dto, context.CancellationToken);

        var reply = new CookingDeductionReply
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cooking inventory deducted successfully.",
            StockIssueId = result.StockIssueId.ToString()
        };

        reply.Movements.AddRange(result.Movements.Select(InventoryMapping.Map));
        reply.LowStockAlerts.AddRange(result.LowStockAlerts.Select(alert => new LowStockAlert
        {
            IngredientId = alert.IngredientId.ToString(),
            IngredientName = alert.IngredientName,
            CurrentStock = alert.CurrentStock.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            Threshold = alert.Threshold.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
        }));

        return reply;
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
