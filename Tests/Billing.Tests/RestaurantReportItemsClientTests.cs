using Backend.Contracts.Ordering;
using Billing.API.Clients;
using Billing.API.Settings;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class RestaurantReportItemsClientTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Unimplemented)]
    public async Task UnavailableDependencyReturnsMissingChannelsWithoutRetry(StatusCode code)
    {
        var transport = new Transport((request, _) => throw new RpcException(new Status(code, "test")));
        var result = await Client(transport).GetItemsAsync(Guid.NewGuid(), [Guid.NewGuid()], default);
        Assert.Empty(result);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    public async Task AuthorizationErrorsAreNeverConvertedIntoSuccessfulReports(StatusCode code)
    {
        var transport = new Transport((request, _) => throw new RpcException(new Status(code, "test")));
        await Assert.ThrowsAsync<RpcException>(() => Client(transport).GetItemsAsync(Guid.NewGuid(), [Guid.NewGuid()], default));
    }

    [Fact]
    public async Task BatchesHaveOneSharedDeadlineForwardAuthorizationAndKeepRequestedOrderSnapshots()
    {
        var restaurant = Guid.NewGuid();
        DateTime? deadline = null;
        var transport = new Transport((request, options) =>
        {
            Assert.InRange(request.OrderIds.Count, 1, 100);
            Assert.Equal("Bearer test", options.Headers!.Single(value => value.Key == "authorization").Value);
            deadline ??= options.Deadline;
            Assert.Equal(deadline, options.Deadline);
            var reply = new ReportOrdersReply
            {
                RestaurantId = restaurant.ToString()
            };
            reply.Items.AddRange(request.OrderIds.Select(id => new ReportOrderSnapshotInfo
            {
                OrderId = id,
                Channel = "Takeaway"
            }));
            return reply;
        });
        var ids = Enumerable.Range(0, 205).Select(_ => Guid.NewGuid()).ToArray();
        var result = await Client(transport).GetItemsAsync(restaurant, ids, default);
        Assert.Equal(205, result.Count);
        Assert.Equal(3, transport.Calls);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("foreign-order")]
    [InlineData("duplicate-id")]
    [InlineData("bad-item-id")]
    public async Task MalformedOrUnrelatedResponsesNeverBecomeOrderSnapshots(string condition)
    {
        var restaurant = Guid.NewGuid();
        var order = Guid.NewGuid();
        var transport = new Transport((_, _) =>
        {
            var reply = new ReportOrdersReply
            {
                RestaurantId = condition == "wrong-owner" ? Guid.NewGuid().ToString() : restaurant.ToString()
            };
            reply.Items.Add(new ReportOrderSnapshotInfo
            {
                OrderId = condition == "foreign-order" ? Guid.NewGuid().ToString() : order.ToString(),
                Channel = "DineIn"
            });
            if (condition == "bad-item-id")
            {
                reply.Items[0].Items.Add(new ReportOrderItemInfo
                {
                    OrderItemId = "bad",
                    ItemId = Guid.NewGuid().ToString(),
                    UnitPrice = "10"
                });
            }

            if (condition == "duplicate-id")
            {
                reply.Items.Add(new ReportOrderSnapshotInfo
                {
                    OrderId = order.ToString("N"),
                    Channel = "Takeaway"
                });
            }

            return reply;
        });
        Assert.Empty(await Client(transport).GetItemsAsync(restaurant, [order], default));
    }

    internal static RestaurantReportItemsClient Client(Transport transport)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer test";
        return new RestaurantReportItemsClient(transport, new HttpContextAccessor
        {
            HttpContext = http
        }, Options.Create(new RestaurantAccessSettings
        {
            TimeoutSeconds = 5
        }), NullLogger<RestaurantReportItemsClient>.Instance);
    }

    internal sealed class Transport(Func<OrderReportChannelsRequest, CallOptions, ReportOrdersReply> response)
        : BillingOrderGrpc.BillingOrderGrpcClient
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<ReportOrdersReply> GetReportItemsAsync(OrderReportChannelsRequest request, CallOptions options)
        {
            Calls++;
            Task<ReportOrdersReply> result;
            try
            {
                result = Task.FromResult(response(request, options));
            }
            catch (RpcException error)
            {
                result = Task.FromException<ReportOrdersReply>(error);
            }

            return new AsyncUnaryCall<ReportOrdersReply>(result, Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }
    }
}
