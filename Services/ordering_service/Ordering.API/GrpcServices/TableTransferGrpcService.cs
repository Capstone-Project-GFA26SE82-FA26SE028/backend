using System.Text.Json;
using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Shared.Common.TableTransfers;

namespace Ordering.API.GrpcServices;

public sealed class TableTransferGrpcService(ITableTransferService service) : TableTransferGrpc.TableTransferGrpcBase
{
    public override async Task<TableTransferReply> Execute(TableTransferRequest request, ServerCallContext context)
    {
        var input = JsonSerializer.Deserialize<TableTransferDTO>(request.Payload)
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "Dữ liệu tách/gộp bàn không hợp lệ."));
        return new TableTransferReply
        {
            Payload = JsonSerializer.Serialize(await service.Execute(input, context.CancellationToken))
        };
    }

    public override async Task<TableTransferReply> Get(TableTransferIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OperationId, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã thao tác không hợp lệ."));
        }
        return new TableTransferReply
        {
            Payload = JsonSerializer.Serialize(await service.GetById(id, context.CancellationToken))
        };
    }
}
