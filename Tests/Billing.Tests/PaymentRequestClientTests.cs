using Backend.Contracts.Restaurant;
using Billing.API.Clients;
using Billing.API.Settings;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class PaymentRequestClientTests
{
    [Fact]
    public async Task ReviewedWaiterComesFromVerifiedRestaurantReplyAndForwardsEmployeeCredential()
    {
        var session = Guid.NewGuid();
        var id = Guid.NewGuid();
        var waiter = Guid.NewGuid();
        var client = Client((method, body, options) =>
        {
            Assert.EndsWith("/GetById", method);
            var request = Assert.IsType<PaymentServiceRequest>(body);
            Assert.Equal(session.ToString(), request.TableSessionId);
            Assert.Equal(id.ToString(), request.ServiceRequestId);
            Assert.Equal(string.Empty, request.GuestToken);
            Assert.Equal("Bearer test-employee", Assert.Single(options.Headers!).Value);
            Assert.NotNull(options.Deadline);
            return new ServiceRequestInfo
            {
                ServiceRequestId = id.ToString(),
                TableSessionId = session.ToString(),
                Type = "Payment",
                Status = "Reviewed",
                HandledByRef = waiter.ToString(),
                AcknowledgedAt = DateTimeOffset.UtcNow.ToString("O")
            };
        });
        Assert.Equal(waiter, await client.GetReviewedWaiterAsync(session, id, default));
    }

    [Theory]
    [InlineData("Acknowledged", true, true, 409)]
    [InlineData("Reviewed", false, true, 409)]
    [InlineData("Reviewed", true, false, 503)]
    public async Task InvalidOrUnreviewedRestaurantReplyCannotAuthorizeRouting(string status, bool owner, bool correctSession, int expected)
    {
        var session = Guid.NewGuid();
        var id = Guid.NewGuid();
        var client = Client((_, _, _) =>
        {
            var reply = new ServiceRequestInfo
            {
                ServiceRequestId = id.ToString(),
                TableSessionId = (correctSession ? session : Guid.NewGuid()).ToString(),
                Type = "Payment",
                Status = status,
                AcknowledgedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            if (owner)
            {
                reply.HandledByRef = Guid.NewGuid().ToString();
            }

            return reply;
        });
        var error = await Assert.ThrowsAsync<BillingException>(() => client.GetReviewedWaiterAsync(session, id, default));
        Assert.Equal(expected, error.StatusCode);
    }

    [Fact]
    public async Task LegacyMissingNotificationDoesNotPreventRetryingCancellationButCannotAuthorizeReview()
    {
        var client = Client((_, _, _) => throw new RpcException(new Status(StatusCode.NotFound, "Missing request")));
        await client.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal(404, (await Assert.ThrowsAsync<BillingException>(() =>
            client.GetReviewedWaiterAsync(Guid.NewGuid(), Guid.NewGuid(), default))).StatusCode);
    }

    private static PaymentRequestClient Client(Func<string, object, CallOptions, object> reply)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test-employee";
        return new PaymentRequestClient(new ServiceRequestGrpc.ServiceRequestGrpcClient(new BillingUnaryInvoker(reply)),
            new HttpContextAccessor
            {
                HttpContext = context
            }, Options.Create(new RestaurantAccessSettings
            {
                TimeoutSeconds = 5
            }));
    }
}
