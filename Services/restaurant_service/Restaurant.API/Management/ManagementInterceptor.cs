using Grpc.Core;
using Grpc.Core.Interceptors;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class ManagementInterceptor(ILogger<ManagementInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (ManagementException ex)
        {
            var status = ex.StatusCode switch
            {
                400 => StatusCode.InvalidArgument,
                401 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                404 => StatusCode.NotFound,
                409 => StatusCode.FailedPrecondition,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(status, ex.Message));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Yêu cầu đã hủy."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restaurant RPC failed: {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Không xử lý được yêu cầu nhà hàng."));
        }
    }
}
