using Billing.Common.Exceptions;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Billing.API.GrpcServices;

public sealed class BillingInterceptor(ILogger<BillingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (BillingException error)
        {
            var status = error.StatusCode switch
            {
                400 => StatusCode.InvalidArgument,
                401 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                404 => StatusCode.NotFound,
                409 => StatusCode.AlreadyExists,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(status, error.Message));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "The request was cancelled."));
        }
        catch (Exception error)
        {
            logger.LogError(error, "Billing RPC failed: {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Unable to process the billing request."));
        }
    }
}
