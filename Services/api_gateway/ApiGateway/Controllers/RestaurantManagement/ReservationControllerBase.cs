using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.RestaurantManagement;

public abstract class ReservationControllerBase : ControllerBase
{
    protected CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        return new CallOptions(headers: headers, deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: ct);
    }

    protected async Task<IActionResult> Execute(Func<Task<object>> action, CancellationToken ct, string message, int status = 200)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return StatusCode(status, new ResponseDTO(message, status, true, await action()));
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            var code = exception.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.Aborted => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            var detail = code switch
            {
                401 => "The account or login session is invalid.",
                403 => "You do not have permission to perform this operation at this restaurant.",
                503 when exception.Status.Detail.StartsWith("Configure valid reservation", StringComparison.Ordinal)
                    || exception.Status.Detail.StartsWith("The restaurant business time zone", StringComparison.Ordinal)
                    => exception.Status.Detail,
                >= 500 => "The restaurant service is unavailable or could not process the request.",
                _ => exception.Status.Detail
            };
            return StatusCode(code, new ResponseDTO(detail, code));
        }
    }

}
