using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Inventory;

public abstract class InventoryControllerBase : ControllerBase
{
    protected CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }
        return new CallOptions(headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct);
    }

    protected async Task<IActionResult> Execute(Func<Task<object>> action, CancellationToken ct, int status = 200)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return StatusCode(status, new ResponseDTO("Thành công.", status, true, await action()));
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            var code = exception.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.Aborted or Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.AlreadyExists => 409,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            return StatusCode(code, new ResponseDTO(code >= 500 ? "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau." : exception.Status.Detail, code));
        }
    }
}
