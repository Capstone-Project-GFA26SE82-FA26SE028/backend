using Backend.Contracts.Ordering;
using Billing.API.Clients;
using Billing.API.Settings;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class RestaurantReportOrderClientTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Unimplemented)]
    public async Task UnavailableDependencyReturnsMissingChannelsWithoutRetry(StatusCode code)
    {
        var transport = new Transport((request, _) => throw new RpcException(new Status(code, "test")));
        var result = await Client(transport).GetChannelsAsync(Guid.NewGuid(), [Guid.NewGuid()], default);
        Assert.Empty(result);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    public async Task AuthorizationErrorsAreNeverConvertedIntoSuccessfulReports(StatusCode code)
    {
        var transport = new Transport((request, _) => throw new RpcException(new Status(code, "test")));
        await Assert.ThrowsAsync<RpcException>(() => Client(transport).GetChannelsAsync(Guid.NewGuid(), [Guid.NewGuid()], default));
    }

    [Fact]
    public async Task BatchesHaveOneSharedDeadlineForwardAuthorizationAndReturnOnlyKnownChannels()
    {
        var restaurant = Guid.NewGuid();
        DateTime? deadline = null;
        var transport = new Transport((request, options) =>
        {
            Assert.InRange(request.OrderIds.Count, 1, 100);
            Assert.Equal("Bearer test", options.Headers!.Single(value => value.Key == "authorization").Value);
            deadline ??= options.Deadline;
            Assert.Equal(deadline, options.Deadline);
            var reply = new OrderReportChannelsReply
            {
                RestaurantId = restaurant.ToString()
            };
            reply.Items.AddRange(request.OrderIds.Select(id => new OrderReportChannelInfo
            {
                OrderId = id,
                Channel = "Takeaway"
            }));
            return reply;
        });
        var ids = Enumerable.Range(0, 205).Select(_ => Guid.NewGuid()).ToArray();
        var result = await Client(transport).GetChannelsAsync(restaurant, ids, default);
        Assert.Equal(205, result.Count);
        Assert.Equal(3, transport.Calls);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("foreign-order")]
    [InlineData("duplicate-id")]
    [InlineData("unknown-channel")]
    public async Task MalformedOrUnrelatedResponsesNeverBecomeAChannel(string condition)
    {
        var restaurant = Guid.NewGuid();
        var order = Guid.NewGuid();
        var transport = new Transport((_, _) =>
        {
            var reply = new OrderReportChannelsReply
            {
                RestaurantId = condition == "wrong-owner" ? Guid.NewGuid().ToString() : restaurant.ToString()
            };
            reply.Items.Add(new OrderReportChannelInfo
            {
                OrderId = condition == "foreign-order" ? Guid.NewGuid().ToString() : order.ToString(),
                Channel = condition == "unknown-channel" ? "Delivery" : "DineIn"
            });
            if (condition == "duplicate-id")
            {
                reply.Items.Add(new OrderReportChannelInfo
                {
                    OrderId = order.ToString("N"),
                    Channel = "Takeaway"
                });
            }

            return reply;
        });
        Assert.Empty(await Client(transport).GetChannelsAsync(restaurant, [order], default));
    }

    internal static RestaurantReportOrderClient Client(Transport transport)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer test";
        return new RestaurantReportOrderClient(transport, new HttpContextAccessor
        {
            HttpContext = http
        }, Options.Create(new RestaurantAccessSettings
        {
            TimeoutSeconds = 5
        }), NullLogger<RestaurantReportOrderClient>.Instance);
    }

    internal sealed class Transport(Func<OrderReportChannelsRequest, CallOptions, OrderReportChannelsReply> response)
        : BillingOrderGrpc.BillingOrderGrpcClient
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<OrderReportChannelsReply> GetReportChannelsAsync(OrderReportChannelsRequest request, CallOptions options)
        {
            Calls++;
            Task<OrderReportChannelsReply> result;
            try
            {
                result = Task.FromResult(response(request, options));
            }
            catch (RpcException error)
            {
                result = Task.FromException<OrderReportChannelsReply>(error);
            }

            return new AsyncUnaryCall<OrderReportChannelsReply>(result, Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }
}
