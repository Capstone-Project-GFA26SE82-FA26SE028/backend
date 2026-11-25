using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;

namespace Restaurant.API.GrpcServices;

public sealed class BillingSessionGrpcService(IBillingSessionService service) : BillingSessionGrpc.BillingSessionGrpcBase
{
    public override async Task<BillingSessionDisplayReply> GetDisplays(BillingSessionDisplayRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId)
            || request.TableSessionIds.Any(value => !Guid.TryParse(value, out _)))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã nhà hàng hoặc phiên bàn không hợp lệ."));
        }

        var result = await service.GetDisplays(restaurantId, request.TableSessionIds.Select(Guid.Parse).ToArray(), context.CancellationToken);
        var reply = new BillingSessionDisplayReply();
        foreach (var session in result)
        {
            var item = new BillingSessionDisplayInfo
            {
                TableSessionId = session.TableSessionId.ToString(),
                RestaurantId = session.RestaurantId.ToString(),
                PartySize = session.PartySize,
                OpenedAt = session.OpenedAt.ToString("O", CultureInfo.InvariantCulture)
            };
            item.Tables.AddRange(session.Tables.Select(table => new BillingTableDisplayInfo
            {
                TableId = table.TableId.ToString(),
                TableCode = table.TableCode,
                AreaId = table.AreaId.ToString(),
                AreaName = table.AreaName
            }));
            reply.Items.Add(item);
        }

        return reply;
    }

    public override async Task<BillingSessionReply> GetById(BillingSessionRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã phiên bàn không hợp lệ hoặc bị trống."));
        }

        var result = await service.GetById(id, context.CancellationToken);
        var reply = new BillingSessionReply
        {
            TableSessionId = result.TableSessionId.ToString(),
            RestaurantId = result.RestaurantId.ToString()
        };
        if (result.CustomerRef.HasValue)
        {
            reply.CustomerRef = result.CustomerRef.Value.ToString();
        }
        return reply;
    }
}
