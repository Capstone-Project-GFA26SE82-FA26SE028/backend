using Grpc.Core;

namespace Billing.Tests;

internal sealed class BillingUnaryInvoker(Func<string, object, CallOptions, object> reply) : CallInvoker
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Task<TResponse> result;
        try
        {
            result = Task.FromResult((TResponse)reply(method.FullName, request, options));
        }
        catch (Exception error)
        {
            result = Task.FromException<TResponse>(error);
        }

        return new AsyncUnaryCall<TResponse>(result, Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
            });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw new NotSupportedException();
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        throw new NotSupportedException();
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
    {
        throw new NotSupportedException();
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw new NotSupportedException();
    }
}
