using Backend.Contracts.Restaurant;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Restaurant.API.Management;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class RestaurantGrpcService(IRestaurantService service) : RestaurantGrpc.RestaurantGrpcBase
{
    public override async Task<RestaurantPage> GetAll(ListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(ManagementMapping.ToDto(request), context.CancellationToken);
        var reply = new RestaurantPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(ManagementMapping.ToWire));
        return reply;
    }

    public override async Task<RestaurantInfo> GetById(IdRequest request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.GetById(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<RestaurantInfo> Create(RestaurantInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Create(ManagementMapping.ToDto(request), context.CancellationToken));
    }

    public override async Task<RestaurantInfo> Update(RestaurantInfo request, ServerCallContext context)
    {
        return ManagementMapping.ToWire(await service.Update(ManagementMapping.Id(request.RestaurantId),
            ManagementMapping.ToDto(request), context.CancellationToken));
    }
}
