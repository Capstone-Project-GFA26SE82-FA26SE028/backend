using System.Globalization;
using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Grpc.Core;
using Shared.Common.Pagination;

namespace Catalog.API.GrpcServices;

public sealed class MenuGrpcService(IMenuService service) : MenuGrpc.MenuGrpcBase
{
    public override async Task<MenuListReply> GetAll(MenuListRequest request, ServerCallContext context)
    {
        var response = await service.GetAll(new MenuQueryDTO
        {
            CategoryId = request.HasCategoryId ? ParseId(request.CategoryId) : null,
            Search = request.HasSearch ? request.Search : null,
            Channel = request.Channel,
            Page = request.Page,
            PageSize = request.PageSize
        }, SessionId(request.Access), request.Access?.GuestToken, context.CancellationToken);
        var reply = new MenuListReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is PagedResult<MenuItemDTO> page)
        {
            reply.Items.AddRange(page.Items.Select(Map));
            reply.TotalCount = page.TotalCount;
            reply.Page = page.Page;
            reply.PageSize = page.PageSize;
        }

        return reply;
    }

    public override async Task<MenuItemReply> GetById(MenuItemRequest request, ServerCallContext context)
    {
        var response = await service.GetById(ParseId(request.ItemId), request.Channel,
            SessionId(request.Access), request.Access?.GuestToken, context.CancellationToken);
        return new MenuItemReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess,
            Result = response.Result is MenuItemDTO item ? Map(item) : null
        };
    }

    private static Guid? SessionId(MenuAccessRequest? access)
    {
        return access?.HasTableSessionId == true ? ParseId(access.TableSessionId) : null;
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid ID."));
        }

        return id;
    }

    private static MenuItemInfo Map(MenuItemDTO item)
    {
        var result = new MenuItemInfo
        {
            ItemId = item.ItemId.ToString(),
            CategoryId = item.CategoryId.ToString(),
            CategoryName = item.CategoryName,
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Availability = item.Availability,
            Channel = item.Channel,
            EvaluatedAt = item.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture),
            CanOrder = item.CanOrder
        };
        if (item.PreparationTimeMinutes.HasValue)
        {
            result.PreparationTimeMinutes = item.PreparationTimeMinutes.Value;
        }
        if (item.Description is not null)
        {
            result.Description = item.Description;
        }

        if (item.ImageUrl is not null)
        {
            result.ImageUrl = item.ImageUrl;
        }

        if (item.Price.HasValue)
        {
            result.Price = item.Price.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (item.PriceId.HasValue)
        {
            result.PriceId = item.PriceId.Value.ToString();
        }

        if (item.UnavailableReason is not null)
        {
            result.UnavailableReason = item.UnavailableReason;
        }

        foreach (var group in item.OptionGroups)
        {
            var mapped = new MenuOptionGroupInfo
            {
                OptionGroupId = group.OptionGroupId.ToString(),
                OptionGroupName = group.OptionGroupName,
                MinSelection = group.MinSelection,
                MaxSelection = group.MaxSelection
            };
            mapped.Options.AddRange(group.Options.Select(option => new MenuOptionInfo
            {
                OptionId = option.OptionId.ToString(),
                OptionName = option.OptionName,
                ExtraPrice = option.ExtraPrice.ToString(CultureInfo.InvariantCulture),
                Availability = option.Availability,
                CanSelect = option.CanSelect
            }));
            result.OptionGroups.Add(mapped);
        }

        return result;
    }
}
