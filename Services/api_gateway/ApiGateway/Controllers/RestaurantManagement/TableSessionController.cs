using System.Globalization;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions")]
public sealed class TableSessionController(TableSessionGrpc.TableSessionGrpcClient client) : ManagementControllerBase
{
    [HttpPost("tables/{tableId:guid}/open")]
    [ApiGateway.Swagger.RequireBearerToken]
    public Task<IActionResult> OpenForWaiter(Guid tableId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.OpenForWaiterAsync(new IdRequest
            {
                Id = tableId.ToString()
            }, Options(ct));
            return MapScan(reply);
        }, ct);
    }

    [HttpPost("tables/{tableId:guid}/cancel")]
    [ApiGateway.Swagger.RequireBearerToken]
    public Task<IActionResult> CancelForTable(Guid tableId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.CloseEmptySessionAsync(new IdRequest
            {
                Id = tableId.ToString()
            }, Options(ct));
            return new { success = true, message = "Đã hủy phiên bàn thành công." };
        }, ct);
    }

    [HttpPost("{sessionId:guid}/cancel")]
    [ApiGateway.Swagger.RequireBearerToken]
    public Task<IActionResult> CancelForSession(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.CloseEmptySessionAsync(new IdRequest
            {
                Id = sessionId.ToString()
            }, Options(ct));
            return new { success = true, message = "Đã hủy phiên bàn thành công." };
        }, ct);
    }

    [HttpPost("scan")]
    public Task<IActionResult> Scan(ScanTableSessionDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.ScanAsync(new QrTokenRequest
            {
                Token = body.Token ?? ""
            }, Options(ct));
            return MapScan(reply);
        }, ct);
    }

    private static TableSessionScanDto MapScan(TableSessionScanReply reply)
    {
        return new TableSessionScanDto
        {
            TableSessionId = Guid.Parse(reply.TableSessionId),
            TableId = Guid.Parse(reply.TableId),
            TableCode = reply.TableCode,
            AreaId = Guid.Parse(reply.AreaId),
            RestaurantId = Guid.Parse(reply.RestaurantId),
            RestaurantName = reply.RestaurantName,
            AreaName = reply.AreaName,
            LogoUrl = reply.HasLogoUrl ? reply.LogoUrl : null,
            Status = reply.Status,
            OpenedAt = DateTime.Parse(reply.OpenedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            IsNewSession = reply.IsNewSession
        };
    }
}
