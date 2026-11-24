using System.Security.Cryptography;
using System.Text;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class TableTransferBillingGrpcService(IBillService service, IConfiguration configuration)
    : TableTransferBillingGrpc.TableTransferBillingGrpcBase
{
    public override async Task<TableTransferBillingReply> Check(TableTransferBillingRequest request, ServerCallContext context)
    {
        var expected = configuration["TableTransfer:ServiceKey"];
        var supplied = context.RequestHeaders.GetValue("x-table-transfer-key") ?? "";
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Không có quyền kiểm tra hóa đơn nội bộ."));
        }
        if (!Guid.TryParse(request.SourceSessionId, out var source) || !Guid.TryParse(request.TargetSessionId, out var target)
            || source == Guid.Empty || target == Guid.Empty || source == target)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã phiên bàn không hợp lệ."));
        }
        return new TableTransferBillingReply
        {
            Allowed = await service.CanTransfer(source, target, context.CancellationToken)
        };
    }
}
