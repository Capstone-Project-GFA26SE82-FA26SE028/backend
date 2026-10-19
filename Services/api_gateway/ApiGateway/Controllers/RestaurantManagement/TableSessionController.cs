using System.Globalization;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions")]
public sealed class TableSessionController(TableSessionGrpc.TableSessionGrpcClient client) : ManagementControllerBase
{
    [HttpPost("scan")]
    public Task<IActionResult> Scan(ScanTableSessionDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.ScanAsync(new QrTokenRequest
            {
                Token = body.Token ?? ""
            }, Options(ct));
            return new TableSessionScanDto
            {
                TableSessionId = Guid.Parse(reply.TableSessionId),
                TableId = Guid.Parse(reply.TableId),
                TableCode = reply.TableCode,
                AreaId = Guid.Parse(reply.AreaId),
                RestaurantId = Guid.Parse(reply.RestaurantId),
                Status = reply.Status,
                OpenedAt = DateTime.Parse(reply.OpenedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                IsNewSession = reply.IsNewSession
            };
        }, ct);
    }
}
