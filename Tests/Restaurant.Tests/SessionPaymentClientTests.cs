extern alias restaurantapi;

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Xunit;
using PaymentClient = restaurantapi::Backend.Contracts.Billing.PaymentGrpc.PaymentGrpcClient;
using PaymentRequest = restaurantapi::Backend.Contracts.Billing.DeparturePaymentRequest;
using Adapter = restaurantapi::Restaurant.API.Management.SessionPaymentClient;

namespace Restaurant.Tests;

public sealed class SessionPaymentClientTests
{
    [Theory]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.FailedPrecondition, 409)]
    [InlineData(StatusCode.Aborted, 409)]
    [InlineData(StatusCode.NotFound, 409)]
    [InlineData(StatusCode.Unauthenticated, 401)]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.InvalidArgument, 400)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 503)]
    public async Task BillingErrorsRemainControlledAndExactScopeAndEmployeeTokenAreForwarded(StatusCode status, int expected)
    {
        var session = Guid.NewGuid();
        var restaurant = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test-waiter";
        var client = new FailingClient(session, restaurant, status);
        var service = new Adapter(client, new HttpContextAccessor
        {
            HttpContext = context
        }, Options.Create(new ServiceRequestSettings()));
        var error = await Assert.ThrowsAsync<ManagementException>(() => service.RequirePaidAsync(session, restaurant, default));
        Assert.Equal(expected, error.StatusCode);
        Assert.DoesNotContain("private-bank-error", error.Message);
        Assert.Equal(1, client.Calls);
    }

    private sealed class FailingClient(Guid session, Guid restaurant, StatusCode status) : PaymentClient
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<Empty> RequirePaidAsync(PaymentRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(session.ToString(), request.TableSessionId);
            Assert.Equal(restaurant.ToString(), request.RestaurantId);
            Assert.Equal("Bearer test-waiter", headers?.GetValue("authorization"));
            Assert.NotNull(deadline);
            return new AsyncUnaryCall<Empty>(Task.FromException<Empty>(new RpcException(new Status(status, "private-bank-error"))),
                Task.FromResult(new Metadata()), () => new Status(status, string.Empty), () => new Metadata(), () =>
                {
                });
        }
    }
}
