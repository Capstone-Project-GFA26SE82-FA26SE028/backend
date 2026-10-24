using System.Globalization;
using Backend.Contracts.Catalog;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class CatalogOrderClient(OrderValidationGrpc.OrderValidationGrpcClient client,
    IHttpContextAccessor http, IOptions<OrderingSettings> settings) : ICatalogOrderClient
{
    public async Task<QuoteDTO> ValidateAsync(OrderingActor actor, string guestToken, List<SelectionDTO> items, CancellationToken ct)
    {
        var request = new ValidateOrderItemsRequest
        {
            Channel = "DineIn",
            Access = new MenuAccessRequest()
        };
        if (actor.IsGuest)
        {
            request.Access.TableSessionId = actor.SessionId.ToString();
            request.Access.GuestToken = guestToken;
        }

        foreach (var item in items)
        {
            var selected = new OrderSelection
            {
                ItemId = item.ItemId.ToString(),
                Quantity = item.Quantity
            };
            selected.Options.AddRange(item.Options.Select(option => new OrderOptionSelection
            {
                OptionId = option.OptionId.ToString(),
                Quantity = option.Quantity
            }));
            request.Items.Add(selected);
        }

        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        var reply = await client.ValidateOrderItemsAsync(request, headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!reply.IsSuccess && reply.StatusCode != 409)
        {
            throw new OrderingException(reply.StatusCode is 400 or 401 or 403 ? reply.StatusCode : 503, reply.Message);
        }

        if (reply.Result is null || (reply.IsSuccess && reply.Result.Items.Count != items.Count))
        {
            throw new OrderingException(503, "Invalid catalog quotation response.");
        }

        var result = reply.Result;
        if (reply.IsSuccess)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var line = result.Items[index];
                if (line.LineIndex != index || line.ItemId != items[index].ItemId.ToString() || line.Quantity != items[index].Quantity
                    || line.Options.Count != items[index].Options.Count
                    || line.Options.Any(option => !items[index].Options.Any(selection => selection.OptionId.ToString() == option.OptionId
                        && selection.Quantity == option.Quantity)))
                {
                    throw new OrderingException(503, "Catalog selections do not match the request.");
                }
            }
        }

        return new QuoteDTO
        {
            IsValid = reply.IsSuccess,
            Message = result.Errors.Count > 0
                ? string.Join("; ", result.Errors.Select(error => $"Line {error.LineIndex + 1}: {error.Message}")) : reply.Message,
            EvaluatedAt = DateTimeOffset.Parse(result.EvaluatedAt, CultureInfo.InvariantCulture),
            TotalAmount = result.HasTotalAmount ? decimal.Parse(result.TotalAmount, CultureInfo.InvariantCulture) : null,
            Items = result.Items.Select(item => new QuotedItemDTO
            {
                ItemId = Guid.Parse(item.ItemId),
                Name = item.NameSnapshot,
                RecipeVersionId = Guid.Parse(item.RecipeVersionId),
                KitchenStationId = item.HasKitchenStationId ? Guid.Parse(item.KitchenStationId) : null,
                PreparationTimeMinutes = item.HasPreparationTimeMinutes ? item.PreparationTimeMinutes : null,
                Quantity = item.Quantity,
                UnitPrice = decimal.Parse(item.UnitPriceSnapshot, CultureInfo.InvariantCulture),
                Options = item.Options.Select(option => new QuotedOptionDTO
                {
                    OptionId = Guid.Parse(option.OptionId),
                    Name = option.NameSnapshot,
                    Quantity = option.Quantity,
                    ExtraPrice = decimal.Parse(option.ExtraPriceSnapshot, CultureInfo.InvariantCulture)
                }).ToList()
            }).ToList(),
            Errors = result.Errors.Select(error => new QuoteErrorDTO
            {
                LineIndex = error.LineIndex,
                Code = error.Code,
                Message = error.Message
            }).ToList()
        };
    }
}
