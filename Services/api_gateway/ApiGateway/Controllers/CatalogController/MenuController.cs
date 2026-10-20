using System.Globalization;
using ApiGateway.DTOs.Catalog;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace ApiGateway.Controllers.CatalogController;

[ApiController]
[Route("api/menu")]
[RequireBearerToken(Optional = true)]
public sealed class MenuController(
    MenuGrpc.MenuGrpcClient client,
    IOptions<CatalogClientSettings> settings) : CatalogControllerBase(settings)
{
    [HttpGet]
    public Task<IActionResult> GetAll(
        [FromQuery] MenuQueryDTO query, [FromQuery] Guid? tableSessionId, CancellationToken cancellationToken)
    {
        return Execute(async () =>
        {
            var request = new MenuListRequest
            {
                Access = Access(tableSessionId),
                Channel = query.Channel,
                Page = query.Page,
                PageSize = query.PageSize
            };
            if (query.CategoryId.HasValue)
            {
                request.CategoryId = query.CategoryId.Value.ToString();
            }

            if (query.Search is not null)
            {
                request.Search = query.Search;
            }

            var reply = await client.GetAllAsync(request, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.IsSuccess
                    ? new PagedResult<MenuItemDTO>(reply.Items.Select(Map).ToArray(),
                        reply.TotalCount, reply.Page, reply.PageSize)
                    : null
            });
        }, cancellationToken);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(
        Guid id, [FromQuery] Guid? tableSessionId, CancellationToken cancellationToken,
        [FromQuery] string channel = "DineIn")
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new MenuItemRequest
            {
                Access = Access(tableSessionId),
                ItemId = id.ToString(),
                Channel = channel
            }, Options(cancellationToken));
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = reply.Result is not null ? Map(reply.Result) : null
            });
        }, cancellationToken);
    }

    private MenuAccessRequest Access(Guid? tableSessionId)
    {
        var access = new MenuAccessRequest();
        if (tableSessionId.HasValue)
        {
            access.TableSessionId = tableSessionId.Value.ToString();
            access.GuestToken = Request.Cookies[$"guest_access_{tableSessionId.Value:N}"] ?? string.Empty;
        }

        return access;
    }

    private static MenuItemDTO Map(MenuItemInfo item)
    {
        return new MenuItemDTO
        {
            ItemId = Guid.Parse(item.ItemId),
            CategoryId = Guid.Parse(item.CategoryId),
            CategoryName = item.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Description = item.HasDescription ? item.Description : null,
            ImageUrl = item.HasImageUrl ? item.ImageUrl : null,
            Availability = item.Availability,
            Price = item.HasPrice ? decimal.Parse(item.Price, CultureInfo.InvariantCulture) : null,
            PriceId = item.HasPriceId ? Guid.Parse(item.PriceId) : null,
            Channel = item.Channel,
            EvaluatedAt = DateTimeOffset.Parse(item.EvaluatedAt, CultureInfo.InvariantCulture),
            CanOrder = item.CanOrder,
            UnavailableReason = item.HasUnavailableReason ? item.UnavailableReason : null,
            OptionGroups = item.OptionGroups.Select(group => new MenuOptionGroupDTO
            {
                OptionGroupId = Guid.Parse(group.OptionGroupId),
                OptionGroupName = group.OptionGroupName,
                MinSelection = group.MinSelection,
                MaxSelection = group.MaxSelection,
                Options = group.Options.Select(option => new MenuOptionDTO
                {
                    OptionId = Guid.Parse(option.OptionId),
                    OptionName = option.OptionName,
                    ExtraPrice = decimal.Parse(option.ExtraPrice, CultureInfo.InvariantCulture),
                    Availability = option.Availability,
                    CanSelect = option.CanSelect
                }).ToList()
            }).ToList()
        };
    }
}
