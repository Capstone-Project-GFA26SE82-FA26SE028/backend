using ApiGateway.Settings;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Billing;

public abstract class BillingControllerBase(IOptions<BillingClientSettings> settings) : ControllerBase
{
    protected Task<IActionResult> Execute(Func<CallOptions, Task<object>> action, CancellationToken ct, int status = 200)
    {
        return ExecuteWithStatus(async options => (await action(options), status), ct);
    }

    protected async Task<IActionResult> ExecuteWithStatus(Func<CallOptions, Task<(object Value, int Status)>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized(new ResponseDTO("A valid employee session is required.", 401));
        }

        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var options = new CallOptions(headers, DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct);
        try
        {
            var result = await action(options);
            return StatusCode(result.Status, new ResponseDTO("Success.", result.Status, true, result.Value));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var code = error.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.Aborted or Grpc.Core.StatusCode.FailedPrecondition => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            var message = code is 502 or 503 or 504
                ? "Billing or a required service is unavailable."
                : error.Status.Detail;
            return StatusCode(code, new ResponseDTO(message, code));
        }
    }
}
