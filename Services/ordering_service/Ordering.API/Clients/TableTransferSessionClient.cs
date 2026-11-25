using System.Text.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Shared.Common.TableTransfers;

namespace Ordering.API.Clients;

public sealed class TableTransferSessionClient(TableTransferSessionGrpc.TableTransferSessionGrpcClient client,
    Backend.Contracts.Billing.TableTransferBillingGrpc.TableTransferBillingGrpcClient billing,
    IHttpContextAccessor http, IConfiguration configuration) : ITableTransferSessionClient
{
    public async Task<bool> CanTransferAsync(Guid source, Guid target, CancellationToken ct)
    {
        var reply = await billing.CheckAsync(new Backend.Contracts.Billing.TableTransferBillingRequest
        {
            SourceSessionId = source.ToString(),
            TargetSessionId = target.ToString()
        }, Headers(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
        return reply.Allowed;
    }

    public async Task<TableTransferResultDTO> PrepareAsync(TableTransferDTO request, CancellationToken ct)
    {
        var response = await client.PrepareAsync(new TableTransferSessionRequest
        {
            Payload = JsonSerializer.Serialize(request)
        }, Headers(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
        return Read(response);
    }

    public async Task<TableTransferResultDTO> FinishAsync(Guid id, bool abort, CancellationToken ct)
    {
        var response = await client.FinishAsync(new TableTransferFinishRequest
        {
            OperationId = id.ToString(),
            Abort = abort
        }, Headers(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
        return Read(response);
    }

    public async Task<TableTransferResultDTO> GetAsync(Guid id, CancellationToken ct)
    {
        var response = await client.GetAsync(new TableTransferFinishRequest
        {
            OperationId = id.ToString()
        }, Headers(), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
        return Read(response);
    }

    private Metadata Headers()
    {
        var headers = new Metadata();
        foreach (var name in new[] { "authorization", "x-restaurant-id" })
        {
            var value = http.HttpContext?.Request.Headers[name].ToString();
            if (!string.IsNullOrEmpty(value))
            {
                headers.Add(name, value);
            }
        }
        headers.Add("x-table-transfer-key", configuration["TableTransfer:ServiceKey"] ?? "");
        return headers;
    }

    private static TableTransferResultDTO Read(TableTransferSessionReply response)
    {
        return JsonSerializer.Deserialize<TableTransferResultDTO>(response.Payload)
            ?? throw new RpcException(new Status(StatusCode.Unavailable, "Không đọc được kết quả chuyển bàn."));
    }
}
