using ApiGateway.Exceptions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Customer;

public abstract class CustomerControllerBase : ControllerBase
{
    protected CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var auth = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth))
        {
            headers.Add("authorization", auth);
        }

        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(15), ct);
    }

    protected async Task<IActionResult> Execute(Func<Task<object?>> action, CancellationToken ct, int status = 200)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await action();
            if (result is IActionResult response)
            {
                return response;
            }

            return StatusCode(status, new ResponseDTO("Thành công.", status, true, result));
        }
        catch (GatewayRequestException ex)
        {
            return StatusCode(ex.StatusCode, new ResponseDTO(ex.Message, ex.StatusCode));
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var code = ex.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.Aborted => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 500
            };
            var detail = code >= 500 ? "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau." : ex.Status.Detail;
            return StatusCode(code, new ResponseDTO(detail, code));
        }
    }
}

