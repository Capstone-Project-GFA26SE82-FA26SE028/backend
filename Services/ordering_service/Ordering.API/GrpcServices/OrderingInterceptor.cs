using Grpc.Core;
using Grpc.Core.Interceptors;
using Ordering.Common.Exceptions;

namespace Ordering.API.GrpcServices;

public sealed class OrderingInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (FormatException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã ID hoặc định dạng số không hợp lệ."));
        }
        catch (OrderingException exception)
        {
            var code = exception.StatusCode switch
            {
                400 => StatusCode.InvalidArgument,
                401 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                404 => StatusCode.NotFound,
                409 => StatusCode.FailedPrecondition,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(code, exception.Message));
        }
    }
}
