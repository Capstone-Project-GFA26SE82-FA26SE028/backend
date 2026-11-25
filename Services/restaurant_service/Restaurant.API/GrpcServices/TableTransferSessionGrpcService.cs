using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Shared.Common.TableTransfers;

namespace Restaurant.API.GrpcServices;

public sealed class TableTransferSessionGrpcService(ITableTransferSessionService service, IConfiguration configuration)
    : TableTransferSessionGrpc.TableTransferSessionGrpcBase
{
    public override async Task<TableTransferSessionReply> Prepare(TableTransferSessionRequest request, ServerCallContext context)
    {
        var input = JsonSerializer.Deserialize<TableTransferDTO>(request.Payload)
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "Dữ liệu tách/gộp bàn không hợp lệ."));
        return Map(await service.Prepare(input, context.CancellationToken));
    }

    public override async Task<TableTransferSessionReply> Finish(TableTransferFinishRequest request, ServerCallContext context)
    {
        var expected = configuration["TableTransfer:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-table-transfer-key") ?? "";
        if (string.IsNullOrWhiteSpace(expected) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Không có quyền hoàn tất chuyển bàn nội bộ."));
        }
        return Map(await service.Finish(Id(request.OperationId), request.Abort, context.CancellationToken));
    }

    public override async Task<TableTransferSessionReply> Get(TableTransferFinishRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.OperationId), context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã thao tác không hợp lệ."));
        }
        return id;
    }

    private static TableTransferSessionReply Map(TableTransferResultDTO value)
    {
        return new TableTransferSessionReply
        {
            Payload = JsonSerializer.Serialize(value)
        };
    }
}
