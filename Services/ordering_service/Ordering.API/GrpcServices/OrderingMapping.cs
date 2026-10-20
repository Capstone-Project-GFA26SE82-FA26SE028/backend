using System.Globalization;
using Backend.Contracts.Ordering;
using Ordering.Common.DTOs;

namespace Ordering.API.GrpcServices;

internal static class OrderingMapping
{
    public static OptionSelectionInfo Map(OptionSelectionDTO value)
    {
        var result = new OptionSelectionInfo
        {
            OptionId = value.OptionId.ToString(),
            Quantity = value.Quantity
        };
        return result;
    }

    public static SelectionInfo Map(SelectionDTO value)
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

        result.Options.AddRange(value.Options.Select(Map));
        return result;
    }

    public static QuotedOptionInfo Map(QuotedOptionDTO value)
    {
        var result = new QuotedOptionInfo
        {
            OptionId = value.OptionId.ToString(),
            Name = value.Name,
            Quantity = value.Quantity,
            ExtraPrice = value.ExtraPrice.ToString(CultureInfo.InvariantCulture)
        };
        return result;
    }

    public static QuotedItemInfo Map(QuotedItemDTO value)
    {
        var result = new QuotedItemInfo
        {
            ItemId = value.ItemId.ToString(),
            Name = value.Name,
            RecipeVersionId = value.RecipeVersionId.ToString(),
            Quantity = value.Quantity,
            UnitPrice = value.UnitPrice.ToString(CultureInfo.InvariantCulture)
        };
        result.Options.AddRange(value.Options.Select(Map));
        if (value.KitchenStationId.HasValue)
        {
            result.KitchenStationId = value.KitchenStationId.Value.ToString();
        }

        return result;
    }

    public static QuoteErrorInfo Map(QuoteErrorDTO value)
    {
        var result = new QuoteErrorInfo
        {
            LineIndex = value.LineIndex,
            Code = value.Code,
            Message = value.Message
        };
        return result;
    }

    public static QuoteInfo Map(QuoteDTO value)
    {
        var result = new QuoteInfo
        {
            IsValid = value.IsValid,
            Message = value.Message,
            EvaluatedAt = value.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (value.TotalAmount.HasValue)
        {
            result.TotalAmount = value.TotalAmount.Value.ToString(CultureInfo.InvariantCulture);
        }

        result.Items.AddRange(value.Items.Select(Map));
        result.Errors.AddRange(value.Errors.Select(Map));
        return result;
    }

    public static CartInfo Map(CartDTO value)
    {
        var result = new CartInfo
        {
            CartId = value.CartId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            ExpiresAt = value.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            Version = value.Version
        };
        result.Items.AddRange(value.Items.Select(Map));
        result.Quote = value.Quote is null ? null : Map(value.Quote);
        return result;
    }

    public static OrderLineInfo Map(OrderLineDTO value)
    {
        var result = new OrderLineInfo
        {
            OrderItemId = value.OrderItemId.ToString(),
            ItemId = value.ItemId.ToString(),
            NameSnapshot = value.NameSnapshot,
            Quantity = value.Quantity,
            UnitPriceSnapshot = value.UnitPriceSnapshot.ToString(CultureInfo.InvariantCulture),
            LineTotal = value.LineTotal.ToString(CultureInfo.InvariantCulture),
            Status = value.Status
        };
        if (value.RecipeVersionId.HasValue)
        {
            result.RecipeVersionId = value.RecipeVersionId.Value.ToString();
        }

        if (value.Note is not null)
        {
            result.Note = value.Note;
        }

        result.Options.AddRange(value.Options.Select(Map));
        return result;
    }

    public static OrderInfo Map(OrderDTO value)
    {
        var result = new OrderInfo
        {
            OrderId = value.OrderId.ToString(),
            OrderNumber = value.OrderNumber,
            TableSessionId = value.TableSessionId.ToString(),
            Source = value.Source,
            Status = value.Status,
            Version = value.Version,
            CreatedAt = value.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            TotalAmount = value.TotalAmount.ToString(CultureInfo.InvariantCulture)
        };
        if (value.CreatedBy.HasValue)
        {
            result.CreatedBy = value.CreatedBy.Value.ToString();
        }

        if (value.ConfirmedBy.HasValue)
        {
            result.ConfirmedBy = value.ConfirmedBy.Value.ToString();
        }

        if (value.ConfirmedAt.HasValue)
        {
            result.ConfirmedAt = value.ConfirmedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        result.Items.AddRange(value.Items.Select(Map));
        return result;
    }

    public static SelectionDTO ReadSelection(SelectionInfo value)
    {
        return new SelectionDTO
        {
            ItemId = Guid.Parse(value.ItemId),
            Quantity = value.Quantity,
            Note = value.HasNote ? value.Note : null,
            Options = value.Options.Select(option => new OptionSelectionDTO
            {
                OptionId = Guid.Parse(option.OptionId),
                Quantity = option.Quantity
            }).ToList()
        };
    }
}
