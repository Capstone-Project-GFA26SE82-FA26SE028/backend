using System.Globalization;
using Backend.Contracts.Inventory;

namespace ApiGateway.Controllers.Inventory;

internal static class InventoryMapping
{
    public static object Map(IngredientInfo value)
    {
        return new
        {
            IngredientId = value.Id,
            IngredientCode = value.Code,
            IngredientName = value.Name,
            value.BaseUnit,
            StockQuantity = decimal.Parse(value.StockQuantity, CultureInfo.InvariantCulture),
            LowStockThreshold = decimal.Parse(value.LowStockThreshold, CultureInfo.InvariantCulture),
            value.IsActive,
            value.IsLowStock,
            value.StockVersion
        };
    }

    public static object Map(ReceiptInfo value)
    {
        return new
        {
            GoodsReceiptId = value.Id,
            ReceiptCode = value.Code,
            value.Status,
            CreatedByRef = value.CreatedBy,
            value.CreatedAt,
            ConfirmAt = value.HasConfirmedAt ? value.ConfirmedAt : null,
            value.Note,
            Items = value.Items.Select(line => new
            {
                GoodsReceiptItemId = line.Id,
                UnitId = string.IsNullOrEmpty(line.UnitId) ? null : line.UnitId,
                InputQuantity = line.HasInputQuantity ? decimal.Parse(line.InputQuantity, CultureInfo.InvariantCulture) : decimal.Parse(line.ReceivedQuantity, CultureInfo.InvariantCulture),
                ConversionFactorSnapshot = line.HasConversionFactor ? decimal.Parse(line.ConversionFactor, CultureInfo.InvariantCulture) : 1m,
                line.IngredientId,
                ReceivedQuantity = decimal.Parse(line.ReceivedQuantity, CultureInfo.InvariantCulture),
                line.ExpiryDate
            }).ToList()
        };
    }

    public static object Map(MovementInfo value)
    {
        return new
        {
            StockMovementId = value.Id,
            StockIssueId = string.IsNullOrEmpty(value.StockIssueId) ? null : value.StockIssueId,
            value.IngredientId,
            GoodsReceiptItemId = value.ReceiptItemId,
            value.MovementType,
            QuantityChange = decimal.Parse(value.QuantityChange, CultureInfo.InvariantCulture),
            PerformedByRef = value.PerformedBy,
            value.Reason,
            value.CreatedAt
        };
    }
}
