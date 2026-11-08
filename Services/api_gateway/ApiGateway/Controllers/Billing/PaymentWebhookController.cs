using System.Text;
using System.Text.Json;
using ApiGateway.Settings;
using Backend.Contracts.Billing;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/payments/payos/webhook")]
public sealed class PaymentWebhookController(PaymentWebhookGrpc.PaymentWebhookGrpcClient client,
    IOptions<PayOsGatewaySettings> settings) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Process([FromBody] JsonElement body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var payload = body.GetRawText();
        if (Encoding.UTF8.GetByteCount(payload) > 65536)
        {
            return BadRequest(new ResponseDTO("Dữ liệu webhook tối đa 64 KiB.", 400));
        }

        try
        {
            var result = await client.ProcessAsync(new PaymentWebhookRequest
            {
                Payload = payload
            }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
            return Ok(new ResponseDTO("Đã nhận webhook thành công.", 200, true, new
            {
                received = result.Received
            }));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var status = error.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.PermissionDenied or Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.Aborted => 409,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                Grpc.Core.StatusCode.Unavailable => 503,
                _ => 502
            };
            return StatusCode(status, new ResponseDTO(status >= 500
                ? "Hệ thống xử lý webhook hiện không khả dụng, vui lòng thử lại sau."
                : "Webhook không thể xác minh hoặc không được chấp nhận.", status));
        }
    }
}
