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
                    (401, "The token or session is invalid."),
                Grpc.Core.StatusCode.PermissionDenied =>
                    (403, "You do not have permission to manage the shared menu."),
                Grpc.Core.StatusCode.InvalidArgument =>
                    (400, "Invalid request data."),
                Grpc.Core.StatusCode.NotFound =>
                    (404, "Category not found."),
                Grpc.Core.StatusCode.AlreadyExists or Grpc.Core.StatusCode.FailedPrecondition
                    or Grpc.Core.StatusCode.Aborted =>
                    (409, "The data has changed or the operation does not meet its preconditions."),
                Grpc.Core.StatusCode.Unavailable =>
                    (503, "Catalog or a required service is unavailable."),
                Grpc.Core.StatusCode.DeadlineExceeded =>
                    (504, "The operation timed out."),
                _ => (502, "Unable to process the response from Catalog.")
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
