using System.Globalization;
using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;

namespace Catalog.API.GrpcServices;

public sealed class OrderValidationGrpcService(IOrderValidationService service) : OrderValidationGrpc.OrderValidationGrpcBase
{
    public override async Task<OrderValidationReply> ValidateOrderItems(
        ValidateOrderItemsRequest request, ServerCallContext context)
    {
        var response = await service.ValidateOrderItems(new ValidateOrderItemsDTO
        {
            Channel = request.Channel,
            Items = request.Items.Select(item => new OrderSelectionDTO
            {
                ItemId = ParseId(item.ItemId),
                Quantity = item.Quantity,
                Options = item.Options.Select(option => new OrderOptionSelectionDTO
                {
                    OptionId = ParseId(option.OptionId),
                    Quantity = option.Quantity
                }).ToList()
            }).ToList()
        }, request.Access?.HasTableSessionId == true ? ParseId(request.Access.TableSessionId) : null,
            request.Access?.GuestToken, context.CancellationToken);
        return new OrderValidationReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is OrderValidationResultDTO result ? Map(result) : null
        };
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid ID."));
        }

        return id;
    }

    private static OrderValidationResult Map(OrderValidationResultDTO result)
    {
        var reply = new OrderValidationResult
        {
            Channel = result.Channel,
            EvaluatedAt = result.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (result.TotalAmount.HasValue)
        {
            reply.TotalAmount = Money(result.TotalAmount.Value);
        }

        foreach (var item in result.Items)
        {
            var mapped = new ValidatedOrderItem
            {
                LineIndex = item.LineIndex,
                ItemId = item.ItemId.ToString(),
                NameSnapshot = item.NameSnapshot,
                PriceId = item.PriceId.ToString(),
                RecipeVersionId = item.RecipeVersionId.ToString(),
                Quantity = item.Quantity,
                UnitPriceSnapshot = Money(item.UnitPriceSnapshot),
                OptionsTotalPerUnit = Money(item.OptionsTotalPerUnit),
                UnitTotal = Money(item.UnitTotal),
                LineTotal = Money(item.LineTotal)
            };
            if (item.PreparationTimeMinutes.HasValue)
            {
                mapped.PreparationTimeMinutes = item.PreparationTimeMinutes.Value;
            }
            if (item.KitchenStationId.HasValue)
            {
                mapped.KitchenStationId = item.KitchenStationId.Value.ToString();
            }

            foreach (var option in item.Options)
            {
                var optionInfo = new ValidatedOrderOption
                {
                    OptionId = option.OptionId.ToString(),
                    NameSnapshot = option.NameSnapshot,
                    ExtraPriceSnapshot = Money(option.ExtraPriceSnapshot),
                    Quantity = option.Quantity
                };
                if (option.RecipeVersionId.HasValue)
                {
                    optionInfo.RecipeVersionId = option.RecipeVersionId.Value.ToString();
                }

                mapped.Options.Add(optionInfo);
            }

            reply.Items.Add(mapped);
        }

        foreach (var error in result.Errors)
        {
            var mapped = new OrderValidationError
            {
                LineIndex = error.LineIndex,
                ItemId = error.ItemId.ToString(),
                Code = error.Code,
                Message = error.Message
            };
            if (error.OptionId.HasValue)
            {
                mapped.OptionId = error.OptionId.Value.ToString();
            }

            if (error.OptionGroupId.HasValue)
            {
                mapped.OptionGroupId = error.OptionGroupId.Value.ToString();
            }

            reply.Errors.Add(mapped);
        }

        return reply;
    }

    private static string Money(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
