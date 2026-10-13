using Shared.Common.DTOs;
using ApiGateway.DTOs;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.IdentityController;

public abstract class IdentityControllerBase : ControllerBase
{
    protected CallOptions Options(CancellationToken ct)
    {
        var headers = new Metadata();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (ip is not null)
        {
            headers.Add("x-client-ip", ip);
        }
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(10), ct);
    }

    protected async Task<IActionResult> Execute(Func<Task<IActionResult>> call, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await call();
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var (status, message) = ex.StatusCode switch
            {
                Grpc.Core.StatusCode.Unauthenticated => (401, "Token hoặc phiên không hợp lệ."),
                Grpc.Core.StatusCode.PermissionDenied => (403, "Không có quyền truy cập."),
                Grpc.Core.StatusCode.InvalidArgument => (400, "Dữ liệu không hợp lệ."),
                Grpc.Core.StatusCode.NotFound => (404, "Không tìm thấy user."),
                Grpc.Core.StatusCode.Unavailable => (503, "Identity service không khả dụng."),
                Grpc.Core.StatusCode.DeadlineExceeded => (504, "Identity service phản hồi quá thời gian."),
                _ => (502, "Không thể xử lý phản hồi từ Identity service.")
            };
            return StatusCode(status, new ResponseDTO { StatusCode = status, Message = message, IsSuccess = false });
        }
    }
}
