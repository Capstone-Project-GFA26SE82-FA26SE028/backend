using AI.Common.Exceptions;
using AI.Common.Settings;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace AI.API.GrpcServices;

public sealed class AIInterceptor(IOptions<AISettings> settings, ILogger<AIInterceptor> logger) : Interceptor
{
    private const string RequestCancellation = "AI.RequestCancellation";

    public static CancellationToken Cancellation(ServerCallContext context)
    {
        return (CancellationToken)context.UserState[RequestCancellation];
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.RequestTimeoutSeconds));
        context.UserState[RequestCancellation] = timeout.Token;
        try
        {
            return await continuation(request, context);
        }
        catch (AIException error)
        {
            throw new RpcException(new Status(Map(error.StatusCode), error.Message));
        }
        catch (OperationCanceledException)
        {
            throw new RpcException(new Status(context.CancellationToken.IsCancellationRequested ? StatusCode.Cancelled : StatusCode.DeadlineExceeded, "The assistant request was cancelled or exceeded its time limit."));
        }
        catch (RpcException error)
        {
            var status = timeout.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested
                ? StatusCode.DeadlineExceeded
                : error.StatusCode switch
                {
                    StatusCode.Unauthenticated => StatusCode.Unauthenticated,
                    StatusCode.PermissionDenied => StatusCode.PermissionDenied,
                    StatusCode.NotFound => StatusCode.NotFound,
                    StatusCode.DeadlineExceeded => StatusCode.DeadlineExceeded,
                    _ => StatusCode.Unavailable
                };
            logger.LogWarning("Assistant dependency failed with status {Status}.", error.StatusCode);
            throw new RpcException(new Status(status, "Guest access or a required restaurant service could not be verified. Please try again or contact staff."));
        }
        catch (FormatException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Provide valid identifiers."));
        }
        catch (Exception error)
        {
            logger.LogError("Assistant processing failed with error type {ErrorType}.", error.GetType().Name);
            throw new RpcException(new Status(StatusCode.Internal, "The assistant could not process the request."));
        }
        finally
        {
            context.UserState.Remove(RequestCancellation);
        }
    }

    private static StatusCode Map(int code)
    {
        return code switch
        {
            400 => StatusCode.InvalidArgument,
            401 => StatusCode.Unauthenticated,
            403 => StatusCode.PermissionDenied,
            404 => StatusCode.NotFound,
            409 => StatusCode.FailedPrecondition,
            413 => StatusCode.OutOfRange,
            429 => StatusCode.ResourceExhausted,
            503 => StatusCode.Unavailable,
            504 => StatusCode.DeadlineExceeded,
            _ => StatusCode.Internal
        };
    }
}
