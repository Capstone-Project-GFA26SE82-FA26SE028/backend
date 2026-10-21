using ApiGateway.Settings;
using Backend.Contracts.Ordering;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

public abstract class OrderingControllerBase(IOptions<OrderingClientSettings> settings) : ControllerBase
{
    protected OrderingAccess Access(Guid sessionId)
    {
        return new OrderingAccess
        {
            TableSessionId = sessionId.ToString(),
            GuestToken = string.IsNullOrWhiteSpace(Request.Headers.Authorization)
                ? Request.Cookies[$"guest_access_{sessionId:N}"] ?? string.Empty : string.Empty
        };
    }

    protected CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        return new CallOptions(headers, deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
    }

    protected async Task<IActionResult> Execute(Func<Task<IActionResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await action();
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            var status = exception.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.Aborted or Grpc.Core.StatusCode.AlreadyExists => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            return StatusCode(status, new ResponseDTO(status is 502 or 503 or 504
                ? "Ordering or a required service is unavailable. Retry with the same idempotency key."
                : exception.Status.Detail, status));
        }
    }

    protected IActionResult Result(object? value, int status = 200)
    {
        return StatusCode(status, new ResponseDTO("Success.", status, true, value));
    }

    protected static bool ValidLines(List<ApiGateway.DTOs.Ordering.SelectionDTO>? lines)
    {
        return lines is not null && lines.Count <= 100 && lines.All(line => line is not null
            && line.Options is not null && line.Options.Count <= 100 && line.Options.All(option => option is not null));
    }
}
