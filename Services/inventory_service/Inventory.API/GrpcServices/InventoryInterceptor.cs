using Grpc.Core;
using Grpc.Core.Interceptors;
using Inventory.Common;

namespace Inventory.API.GrpcServices;

public sealed class InventoryInterceptor(ILogger<InventoryInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (InventoryException exception)
        {
            var code = exception.StatusCode switch
            {
                400 => StatusCode.InvalidArgument,
                401 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                404 => StatusCode.NotFound,
                409 => StatusCode.Aborted,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(code, exception.Message));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Request cancelled."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory request failed: {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Inventory request failed."));
        }
    }
}
