using ApiGateway.DTOs.Ordering;
using System.Text.Json;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.TableTransfers;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[RequireBearerToken]
public sealed class TableTransferController(TableTransferGrpc.TableTransferGrpcClient client,
    IOptions<OrderingClientSettings> settings) : OrderingControllerBase(settings)
{
    [HttpPost("/api/table-sessions/{sessionId:guid}/split")]
    public Task<IActionResult> Split(Guid sessionId, TableSplitRequest body,
        [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct)
    {
        return Send(sessionId, key, "Split", body.TargetTableId, body.Reason, body.Items, ct);
    }

    [HttpPost("/api/table-sessions/{sessionId:guid}/merge")]
    public Task<IActionResult> Merge(Guid sessionId, TableMergeRequest body,
        [FromHeader(Name = "Idempotency-Key")] Guid key, CancellationToken ct)
    {
        return Send(sessionId, key, "Merge", body.TargetTableId, body.Reason, [], ct);
    }

    [HttpGet("/api/table-transfers/{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAsync(new TableTransferIdRequest
            {
                OperationId = id.ToString()
            }, Options(ct));
            return Result(JsonSerializer.Deserialize<TableTransferResultDTO>(reply.Payload));
        }, ct);
    }

    private Task<IActionResult> Send(Guid sessionId, Guid key, string mode, Guid table,
        string reason, List<TableTransferLineDTO> items, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new TableTransferDTO
            {
                OperationId = key,
                SourceSessionId = sessionId,
                TargetTableId = table,
                Mode = mode,
                Reason = reason,
                Items = items
            };
            var reply = await client.ExecuteAsync(new TableTransferRequest
            {
                Payload = JsonSerializer.Serialize(request)
            }, Options(ct));
            return Result(JsonSerializer.Deserialize<TableTransferResultDTO>(reply.Payload));
        }, ct);
    }
}
