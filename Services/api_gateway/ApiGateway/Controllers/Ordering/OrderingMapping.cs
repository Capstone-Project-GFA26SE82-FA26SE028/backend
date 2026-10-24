using System.Globalization;
using ApiGateway.DTOs.Ordering;
using Backend.Contracts.Ordering;

namespace ApiGateway.Controllers.Ordering;

internal static class OrderingMapping
{
    public static OptionSelectionDTO Map(OptionSelectionInfo value)
    {
        return new OptionSelectionDTO
        {
            OptionId = Guid.Parse(value.OptionId),
            Quantity = value.Quantity
        };
    }

    public static SelectionDTO Map(SelectionInfo value)
    {
        return new SelectionDTO
        {
            ItemId = Guid.Parse(value.ItemId),
            Quantity = value.Quantity,
            Note = value.HasNote ? value.Note : null,
            Options = value.Options.Select(Map).ToList()
        };
    }

    public static QuotedOptionDTO Map(QuotedOptionInfo value)
    {
        return new QuotedOptionDTO
        {
            OptionId = Guid.Parse(value.OptionId),
            Name = value.Name,
            Quantity = value.Quantity,
            ExtraPrice = decimal.Parse(value.ExtraPrice, CultureInfo.InvariantCulture)
        };
    }

    public static QuotedItemDTO Map(QuotedItemInfo value)
    {
        return new QuotedItemDTO
        {
            ItemId = Guid.Parse(value.ItemId),
            Name = value.Name,
            RecipeVersionId = Guid.Parse(value.RecipeVersionId),
            KitchenStationId = value.HasKitchenStationId ? Guid.Parse(value.KitchenStationId) : null,
            Quantity = value.Quantity,
            UnitPrice = decimal.Parse(value.UnitPrice, CultureInfo.InvariantCulture),
            Options = value.Options.Select(Map).ToList()
        };
    }

    public static QuoteErrorDTO Map(QuoteErrorInfo value)
    {
        return new QuoteErrorDTO
        {
            LineIndex = value.LineIndex,
            Code = value.Code,
            Message = value.Message
        };
    }

    public static QuoteDTO Map(QuoteInfo value)
    {
        return new QuoteDTO
        {
            IsValid = value.IsValid,
            Message = value.Message,
            EvaluatedAt = DateTimeOffset.Parse(value.EvaluatedAt, CultureInfo.InvariantCulture),
            TotalAmount = value.HasTotalAmount ? decimal.Parse(value.TotalAmount, CultureInfo.InvariantCulture) : null,
            Items = value.Items.Select(Map).ToList(),
            Errors = value.Errors.Select(Map).ToList()
        };
    }

    public static CartDTO Map(CartInfo value)
    {
        return new CartDTO
        {
            CartId = Guid.Parse(value.CartId),
            TableSessionId = Guid.Parse(value.TableSessionId),
            ExpiresAt = DateTimeOffset.Parse(value.ExpiresAt, CultureInfo.InvariantCulture),
            Version = value.Version,
            Items = value.Items.Select(Map).ToList(),
            Quote = value.Quote is null ? null : Map(value.Quote)
        };
    }

    public static OrderLineDTO Map(OrderLineInfo value)
    {
        return new OrderLineDTO
        {
            OrderItemId = Guid.Parse(value.OrderItemId),
            ItemId = Guid.Parse(value.ItemId),
            RecipeVersionId = value.HasRecipeVersionId ? Guid.Parse(value.RecipeVersionId) : null,
            NameSnapshot = value.NameSnapshot,
            Quantity = value.Quantity,
            UnitPriceSnapshot = decimal.Parse(value.UnitPriceSnapshot, CultureInfo.InvariantCulture),
            LineTotal = decimal.Parse(value.LineTotal, CultureInfo.InvariantCulture),
            Note = value.HasNote ? value.Note : null,
            Status = value.Status,
            Options = value.Options.Select(Map).ToList()
        };
    }

    public static OrderDTO Map(OrderInfo value)
    {
        return new OrderDTO
        {
            OrderId = Guid.Parse(value.OrderId),
            OrderNumber = value.OrderNumber,
            TableSessionId = Guid.Parse(value.TableSessionId),
            Channel = value.HasChannel ? value.Channel : "DineIn",
            Source = value.Source,
            Status = value.Status,
            Version = value.Version,
            CreatedBy = value.HasCreatedBy ? Guid.Parse(value.CreatedBy) : null,
            CreatedAt = DateTimeOffset.Parse(value.CreatedAt, CultureInfo.InvariantCulture),
            ConfirmedBy = value.HasConfirmedBy ? Guid.Parse(value.ConfirmedBy) : null,
            ConfirmedAt = value.HasConfirmedAt ? DateTimeOffset.Parse(value.ConfirmedAt, CultureInfo.InvariantCulture) : null,
            TotalAmount = decimal.Parse(value.TotalAmount, CultureInfo.InvariantCulture),
            Items = value.Items.Select(Map).ToList()
        };
    }

    public static SelectionInfo WriteSelection(SelectionDTO value)
    {
        var result = new SelectionInfo
        {
            ItemId = value.ItemId.ToString(),
            Quantity = value.Quantity
        };
        if (value.Note is not null)
        {
            result.Note = value.Note;
        }

        result.Options.AddRange(value.Options.Select(option => new OptionSelectionInfo
        {
            OptionId = option.OptionId.ToString(),
            Quantity = option.Quantity
        }));
        return result;
    }

    public static object Map(KitchenTaskInfo value)
    {
        return new
        {
            KitchenTaskId = Guid.Parse(value.KitchenTaskId),
            KitchenStationId = Guid.Parse(value.KitchenStationId),
            value.KitchenStationCode,
            value.KitchenStationName,
            OrderItemId = Guid.Parse(value.OrderItemId),
            OrderId = Guid.Parse(value.OrderId),
            value.OrderNumber,
            TableSessionId = Guid.Parse(value.TableSessionId),
            OriginalTableId = value.HasOriginalTableId ? Guid.Parse(value.OriginalTableId) : (Guid?)null,
            ItemId = Guid.Parse(value.ItemId),
            value.NameSnapshot,
            value.Quantity,
            Note = value.HasNote ? value.Note : null,
            value.Status,
            value.Priority,
            QueuedAt = DateTimeOffset.Parse(value.QueuedAt, CultureInfo.InvariantCulture),
            CookingAt = value.HasCookingAt ? DateTimeOffset.Parse(value.CookingAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            ReadyAt = value.HasReadyAt ? DateTimeOffset.Parse(value.ReadyAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            EstimatedMinutes = value.HasEstimatedMinutes ? value.EstimatedMinutes : (int?)null,
            DueAt = value.HasDueAt ? DateTimeOffset.Parse(value.DueAt, CultureInfo.InvariantCulture) : (DateTimeOffset?)null,
            value.IsLate,
            value.Version,
            Options = value.Options.Select(o => new
            {
                OptionId = Guid.Parse(o.OptionId),
                o.Name,
                o.Quantity
            }).ToList()
        };
    }

    public static object Map(KitchenSummaryReply value)
    {
        return new
        {
            value.PendingCount,
            value.CookingCount,
            value.ReadyCount,
            value.AvgCookingTimeMinutes
        };
    }

    public static object Map(KitchenBatchGroupInfo value)
    {
        return new
        {
            ItemId = Guid.Parse(value.ItemId),
            value.ItemName,
            KitchenStationId = Guid.Parse(value.KitchenStationId),
            value.KitchenStationName,
            value.TotalQuantity,
            Tasks = value.Tasks.Select(t => new
            {
                KitchenTaskId = Guid.Parse(t.KitchenTaskId),
                OrderId = Guid.Parse(t.OrderId),
                t.OrderNumber,
                t.TableCode,
                t.Quantity,
                Note = t.HasNote ? t.Note : null,
                t.Priority,
                EstimatedMinutes = t.HasEstimatedMinutes ? t.EstimatedMinutes : (int?)null,
                QueuedAt = DateTimeOffset.Parse(t.QueuedAt, CultureInfo.InvariantCulture)
            }).ToList()
        };
    }

    public static object Map(KitchenBatchActionReply value)
    {
        return new
        {
            value.AffectedCount,
            value.Status
        };
    }

    public static object Map(FireCourseReply value)
    {
        return new
        {
            value.ActivatedCount,
            value.Message
        };
    }
}
