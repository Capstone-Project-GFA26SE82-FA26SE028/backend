using ApiGateway.Settings;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.CatalogController;

public abstract class CatalogControllerBase(
    IOptions<CatalogClientSettings> settings) : ControllerBase
{
    protected CallOptions Options(CancellationToken cancellationToken)
    {
        var headers = new Metadata();
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (ipAddress is not null)
        {
            headers.Add("x-client-ip", ipAddress);
        }

        return new CallOptions(
            headers: headers,
            deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds),
            cancellationToken: cancellationToken);
    }

    protected async Task<IActionResult> Execute(
        Func<Task<IActionResult>> action,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await action();
        }
        catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var (statusCode, message) = exception.StatusCode switch
            {
                Grpc.Core.StatusCode.Unauthenticated =>
                    (401, "Token hoặc phiên đăng nhập không hợp lệ."),
                Grpc.Core.StatusCode.PermissionDenied =>
                    (403, "Bạn không có quyền thực hiện thao tác này trên thực đơn."),
                Grpc.Core.StatusCode.InvalidArgument =>
                    (400, "Dữ liệu yêu cầu không hợp lệ."),
                Grpc.Core.StatusCode.NotFound =>
                    (404, "Không tìm thấy dữ liệu yêu cầu."),
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.FailedPrecondition
                    or Grpc.Core.StatusCode.Aborted =>
                    (409, "Dữ liệu đã thay đổi hoặc thao tác không thỏa mãn điều kiện tiên quyết."),
                Grpc.Core.StatusCode.Unavailable =>
                    (503, "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau."),
                Grpc.Core.StatusCode.DeadlineExceeded =>
                    (504, "Hết thời gian chờ phản hồi từ hệ thống, vui lòng thử lại sau."),
                _ => (502, "Không thể xử lý yêu cầu lúc này, vui lòng thử lại sau.")
            };

            return StatusCode(statusCode, new ResponseDTO
            {
                StatusCode = statusCode,
                Message = message,
                IsSuccess = false
            });
        }
    }
}
