using System.Globalization;
using Backend.Contracts.Inventory;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL.Models;

namespace Inventory.API.GrpcServices;

internal static class InventoryMapping
{
    public static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new InventoryException(400, "Mã ID không hợp lệ.");
        }
        return id;
    }

    public static decimal Decimal(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number))
        {
            throw new InventoryException(400, "Số lượng thập phân không hợp lệ.");
        }
        return number;
    }

    public static DateOnly Date(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InventoryException(400, "Hạn sử dụng phải theo định dạng yyyy-MM-dd.");
        }
        return date;
    }

    public static IngredientInfo Map(Ingredient value)
    {
        return new IngredientInfo
        {
            Id = value.IngredientId.ToString(),
            Code = value.IngredientCode,
            Name = value.IngredientName,
            BaseUnit = value.BaseUnit,
            StockQuantity = value.StockQuantity.ToString(CultureInfo.InvariantCulture),
            LowStockThreshold = value.LowStockThreshold.ToString(CultureInfo.InvariantCulture),
            IsActive = value.IsActive,
            IsLowStock = value.StockQuantity <= value.LowStockThreshold,
            StockVersion = Convert.ToBase64String(value.StockVersion ?? [])
        };
    }

    public static ReceiptInfo Map(GoodsReceipt value)
    {
        var reply = new ReceiptInfo
        {
            Id = value.GoodsReceiptId.ToString(),
            Code = value.ReceiptCode,
            Status = value.Status,
            CreatedBy = value.CreatedByRef.ToString(),
            CreatedAt = DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc).ToString("O"),
            Note = value.Note ?? ""
        };
        if (value.ConfirmAt.HasValue)
        {
            reply.ConfirmedAt = DateTime.SpecifyKind(value.ConfirmAt.Value, DateTimeKind.Utc).ToString("O");
        }
        reply.Items.AddRange(value.GoodsReceiptItems.OrderBy(x => x.GoodsReceiptItemId).Select(line => new ReceiptLine
        {
            Id = line.GoodsReceiptItemId.ToString(),
            UnitId = line.InputUnitId?.ToString() ?? "",
            InputQuantity = (line.InputQuantity ?? line.ReceivedQuantity).ToString(CultureInfo.InvariantCulture),
            ConversionFactor = (line.ConversionFactorSnapshot ?? 1m).ToString(CultureInfo.InvariantCulture),
            IngredientId = line.IngredientId.ToString(),
            ReceivedQuantity = line.ReceivedQuantity.ToString(CultureInfo.InvariantCulture),
            ExpiryDate = line.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
        }));
        return reply;
    }

    public static MovementInfo Map(StockMovement value)
    {
        var info = new MovementInfo
        {
            Id = value.StockMovementId.ToString(),
            StockIssueId = value.StockIssueId?.ToString() ?? "",
            IngredientId = value.IngredientId.ToString(),
            ReceiptItemId = value.GoodsReceiptItemId?.ToString() ?? "",
            MovementType = value.MovementType,
            QuantityChange = value.QuantityChange.ToString(CultureInfo.InvariantCulture),
            PerformedBy = value.PerformedByRef?.ToString() ?? "",
            Reason = value.Reason ?? "",
            CreatedAt = DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc).ToString("O")
        };

        if (value.CookingEventRef.HasValue)
        {
            info.CookingEventRef = value.CookingEventRef.Value.ToString();
        }

        if (value.OrderItemRef.HasValue)
        {
            info.OrderItemRef = value.OrderItemRef.Value.ToString();
        }

        return info;
    }

    public static MovementInfo Map(CookingMovementDTO value)
    {
        var info = new MovementInfo
        {
            Id = value.StockMovementId.ToString(),
            StockIssueId = value.StockIssueId?.ToString() ?? "",
            IngredientId = value.IngredientId.ToString(),
            ReceiptItemId = value.GoodsReceiptItemId?.ToString() ?? "",
            MovementType = value.MovementType,
            QuantityChange = value.QuantityChange.ToString(CultureInfo.InvariantCulture),
            PerformedBy = value.PerformedByRef?.ToString() ?? "",
            Reason = value.Reason ?? "",
            CreatedAt = DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc).ToString("O")
        };

        if (value.CookingEventRef.HasValue)
        {
            info.CookingEventRef = value.CookingEventRef.Value.ToString();
        }

        if (value.OrderItemRef.HasValue)
        {
            info.OrderItemRef = value.OrderItemRef.Value.ToString();
        }

        return info;
    }
}
