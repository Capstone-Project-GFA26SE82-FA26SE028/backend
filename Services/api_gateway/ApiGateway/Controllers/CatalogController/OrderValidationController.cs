using System.Globalization;
using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/menu/validate-order-items")]
[RequireBearerToken(Optional = true)]
public sealed class OrderValidationController(
    OrderValidationGrpc.OrderValidationGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> ValidateOrderItems(
        ValidateOrderItemsDTO body, [FromQuery] Guid? tableSessionId, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            if (body.Items is null || body.Items.Any(item => item is null
                || item.Options is null || item.Options.Any(option => option is null)))
            {
                return BadRequest(new ResponseDTO("Items and options cannot be null.", 400));
            }

            var request = new ValidateOrderItemsRequest
            {
                Access = new MenuAccessRequest(),
                Channel = body.Channel ?? string.Empty
            };
            if (tableSessionId.HasValue)
            {
                request.Access.TableSessionId = tableSessionId.Value.ToString();
                request.Access.GuestToken = Request.Cookies[$"guest_access_{tableSessionId.Value:N}"] ?? string.Empty;
            }

            foreach (var item in body.Items)
            {
                var selection = new OrderSelection
                {
                    ItemId = item.ItemId.ToString(),
                    Quantity = item.Quantity
                };
                selection.Options.AddRange(item.Options.Select(option => new OrderOptionSelection
                {
                    OptionId = option.OptionId.ToString(),
                    Quantity = option.Quantity
                }));
                request.Items.Add(selection);
            }

            var reply = await client.ValidateOrderItemsAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Result is null ? null : Map(reply.Result)
            });
        }, cancellationToken);
    }

    private static OrderValidationResultDTO Map(OrderValidationResult result)
    {
        return new OrderValidationResultDTO
        {
            Channel = result.Channel,
            EvaluatedAt = DateTimeOffset.Parse(result.EvaluatedAt, CultureInfo.InvariantCulture),
            TotalAmount = result.HasTotalAmount ? Money(result.TotalAmount) : null,
            Items = result.Items.Select(item => new ValidatedOrderItemDTO
            {
                LineIndex = item.LineIndex,
                ItemId = Guid.Parse(item.ItemId),
                NameSnapshot = item.NameSnapshot,
                PriceId = Guid.Parse(item.PriceId),
                RecipeVersionId = Guid.Parse(item.RecipeVersionId),
                KitchenStationId = item.HasKitchenStationId ? Guid.Parse(item.KitchenStationId) : null,
                Quantity = item.Quantity,
                UnitPriceSnapshot = Money(item.UnitPriceSnapshot),
                OptionsTotalPerUnit = Money(item.OptionsTotalPerUnit),
                UnitTotal = Money(item.UnitTotal),
                LineTotal = Money(item.LineTotal),
                PreparationTimeMinutes = item.HasPreparationTimeMinutes ? item.PreparationTimeMinutes : null,
                Options = item.Options.Select(option => new ValidatedOrderOptionDTO
                {
                    OptionId = Guid.Parse(option.OptionId),
                    NameSnapshot = option.NameSnapshot,
                    ExtraPriceSnapshot = Money(option.ExtraPriceSnapshot),
                    Quantity = option.Quantity,
                    RecipeVersionId = option.HasRecipeVersionId ? Guid.Parse(option.RecipeVersionId) : null
                }).ToList()
            }).ToList(),
            Errors = result.Errors.Select(error => new OrderValidationErrorDTO
            {
                LineIndex = error.LineIndex,
                ItemId = Guid.Parse(error.ItemId),
                OptionId = error.HasOptionId ? Guid.Parse(error.OptionId) : null,
                OptionGroupId = error.HasOptionGroupId ? Guid.Parse(error.OptionGroupId) : null,
                Code = error.Code,
                Message = error.Message
            }).ToList()
        };
    }

    private static decimal Money(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
