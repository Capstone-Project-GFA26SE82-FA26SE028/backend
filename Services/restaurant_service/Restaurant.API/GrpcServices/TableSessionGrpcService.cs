using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;

namespace Restaurant.API.GrpcServices;

public sealed class TableSessionGrpcService(ITableSessionService service) : TableSessionGrpc.TableSessionGrpcBase
{
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
