using Backend.Contracts.Restaurant;
using Billing.API.Clients;
using Billing.API.Settings;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class RestaurantBillingAccessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeClientForwardsCurrentEmployeeCredentialsAndOptionalBranch(bool filterBranch)
    {
        var userId = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var invoker = new BillingUnaryInvoker((method, request, options) =>
        {
            Assert.Equal("/restaurant.RestaurantAccessGrpc/GetCurrentScope", method);
            Assert.Equal("Bearer current-employee", options.Headers!.Single(value => value.Key == "authorization").Value);
            Assert.InRange(options.Deadline!.Value - DateTime.UtcNow, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6));
            var query = Assert.IsType<CurrentRestaurantScopeRequest>(request);
            Assert.Equal(filterBranch, query.HasRestaurantId);
            if (filterBranch)
            {
                Assert.Equal(branch.ToString(), query.RestaurantId);
            }

            var reply = new CurrentRestaurantScopeReply
            {
                UserId = userId.ToString(),
                RoleId = "cashier"
            };
            reply.RestaurantIds.Add(branch.ToString());
            reply.RestaurantIds.Add(branch.ToString());
            return reply;
        });
        var result = await Client(invoker).GetCurrentScopeAsync(filterBranch ? branch : null, default);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("cashier", result.RoleId);
        Assert.False(result.IsAdministrator);
        Assert.Equal(branch, Assert.Single(result.RestaurantIds));
    }

    [Fact]
    public async Task MissingEmployeeCredentialsAreRejectedWithoutCallingRestaurant()
    {
        var calls = 0;
        var client = Client(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("No credentials must not reach Restaurant.");
        }), authorization: null);
        var error = await Assert.ThrowsAsync<BillingException>(() => client.GetCurrentScopeAsync(null, default));
        Assert.Equal(401, error.StatusCode);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("invalid-user")]
    [InlineData("empty-user")]
    [InlineData("empty-role")]
    [InlineData("invalid-branch")]
    [InlineData("empty-branch")]
    public async Task InvalidReturnedScopeCannotGrantAccess(string invalid)
    {
        var reply = new CurrentRestaurantScopeReply
        {
            UserId = invalid switch
            {
                "invalid-user" => "not-a-guid",
                "empty-user" => Guid.Empty.ToString(),
                _ => Guid.NewGuid().ToString()
            },
            RoleId = invalid == "empty-role" ? string.Empty : "manager"
        };
        reply.RestaurantIds.Add(invalid switch
        {
            "invalid-branch" => "not-a-guid",
            "empty-branch" => Guid.Empty.ToString(),
            _ => Guid.NewGuid().ToString()
        });
        var client = Client(new BillingUnaryInvoker((_, _, _) => reply));
        var error = await Assert.ThrowsAsync<BillingException>(() => client.GetCurrentScopeAsync(null, default));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Unavailable)]
    public async Task RevocationAndRestaurantFailuresCannotFallBackToUnrestrictedScope(StatusCode status)
    {
        var client = Client(new BillingUnaryInvoker((_, _, _) =>
        {
            throw new RpcException(new Status(status, "Access was denied by Restaurant."));
        }));
        var error = await Assert.ThrowsAsync<RpcException>(() => client.GetCurrentScopeAsync(Guid.NewGuid(), default));
        Assert.Equal(status, error.StatusCode);
    }

    private static RestaurantBillingAccess Client(CallInvoker invoker, string? authorization = "Bearer current-employee")
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return new RestaurantBillingAccess(new RestaurantAccessGrpc.RestaurantAccessGrpcClient(invoker),
            new HttpContextAccessor
            {
                HttpContext = context
            }, Options.Create(new RestaurantAccessSettings
            {
                TimeoutSeconds = 5
            }));
    }
}
