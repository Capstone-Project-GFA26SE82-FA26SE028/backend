using Restaurant.API.Management;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;

namespace Restaurant.API.GrpcServices;

public sealed class TableSessionGrpcService(ITableSessionService service) : TableSessionGrpc.TableSessionGrpcBase
{
    public override async Task<TableSessionScanReply> OpenForWaiter(IdRequest request, ServerCallContext context)
    {
        var result = await service.OpenForWaiter(ManagementMapping.Id(request.Id), context.CancellationToken);
        return new TableSessionScanReply
        {
            TableSessionId = result.TableSessionId.ToString(),
            TableId = result.TableId.ToString(),
            TableCode = result.TableCode,
            AreaId = result.AreaId.ToString(),
            RestaurantId = result.RestaurantId.ToString(),
            Status = result.Status,
            OpenedAt = result.OpenedAt.ToString("O"),
            IsNewSession = result.IsNewSession
        };
    }

    public override async Task<OrderSessionReply> GetForOrdering(OrderSessionRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var id) || id == Guid.Empty
            || (request.HasTableId && (!Guid.TryParse(request.TableId, out var parsed) || parsed == Guid.Empty)))
        {
            throw new Restaurant.Common.Exceptions.ManagementException(400, "Invalid session or table ID.");
        }

        var result = await service.GetForOrdering(id, request.HasTableId ? Guid.Parse(request.TableId) : null,
            request.GuestToken, context.CancellationToken);
        var reply = new OrderSessionReply
        {
            TableSessionId = result.TableSessionId.ToString()
        };
        if (result.TableId.HasValue)
        {
            reply.TableId = result.TableId.Value.ToString();
        }

        if (result.UserId.HasValue)
        {
            reply.UserId = result.UserId.Value.ToString();
        }

        if (result.GuestAccessId.HasValue)
        {
            reply.GuestAccessId = result.GuestAccessId.Value.ToString();
            reply.ExpiresAt = DateTime.SpecifyKind(result.ExpiresAt!.Value, DateTimeKind.Utc).ToString("O");
        }

        return reply;
    }

    public override async Task<TableSessionScanReply> Scan(QrTokenRequest request, ServerCallContext context)
    {
        var result = await service.Scan(request.Token, context.CancellationToken);
        return new TableSessionScanReply
        {
            TableSessionId = result.TableSessionId.ToString(),
            TableId = result.TableId.ToString(),
            TableCode = result.TableCode,
            AreaId = result.AreaId.ToString(),
            RestaurantId = result.RestaurantId.ToString(),
            Status = result.Status,
            OpenedAt = result.OpenedAt.ToString("O"),
            IsNewSession = result.IsNewSession
        };
    }
}
