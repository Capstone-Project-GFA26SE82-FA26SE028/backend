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
                401 => "Tài khoản hoặc phiên đăng nhập không hợp lệ.",
                403 => "Bạn không có quyền thực hiện thao tác này tại nhà hàng.",
                503 when exception.Status.Detail.StartsWith("Cấu hình thời lượng đặt bàn", StringComparison.Ordinal)
                    || exception.Status.Detail.StartsWith("Múi giờ kinh doanh của nhà hàng", StringComparison.Ordinal)
                    => exception.Status.Detail,
                >= 500 => "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau.",
                _ => exception.Status.Detail
            };
            return StatusCode(code, new ResponseDTO(detail, code));
        }
    }

}
