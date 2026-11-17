using Backend.Contracts.Restaurant;
using Backend.Contracts.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Ordering.API.Clients;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Xunit;

namespace Ordering.Tests;

public sealed class TakeawayAccessTests
{
    [Theory]
    [InlineData("waiter", true, 200)]
    [InlineData("manager", true, 403)]
    [InlineData("customer", false, 200)]
    [InlineData("waiter", false, 403)]
    [InlineData("kitchen", true, 403)]
    [InlineData("cashier", true, 200)]
    [InlineData("admin", true, 403)]
    [InlineData("", true, 401)]
    public async Task RequiresBearerAllowedRoleAndRestaurantAssignment(string role, bool assigned, int expected)
    {
        var restaurant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var context = new DefaultHttpContext();
        if (role.Length > 0)
        {
            context.Request.Headers.Authorization = "Bearer test";
        }

        var reply = new CurrentRestaurantScopeReply
        {
            UserId = user.ToString(),
            RoleId = role,
            IsAdministrator = role == "admin"
        };
        reply.RestaurantIds.Add(assigned ? restaurant.ToString() : Guid.NewGuid().ToString());
        var client = new ScopeClient(reply);
        var access = new TakeawayAccess(new IdentityClient(user, role), client, new HttpContextAccessor
        {
            HttpContext = context
        }, Options.Create(new OrderingSettings()));
        if (expected == 200)
        {
            Assert.Equal(user, (await access.GetActorAsync(restaurant, default)).UserId);
            if (role != "customer")
            {
                Assert.Equal(restaurant.ToString(), client.RequestedRestaurant);
                Assert.Equal("Bearer test", client.Authorization);
            }
        }
        else
        {
            var error = await Assert.ThrowsAsync<OrderingException>(() => access.GetActorAsync(restaurant, default));
            Assert.Equal(expected, error.StatusCode);
        }
    }

    private sealed class IdentityClient(Guid user, string role) : UserGrpc.UserGrpcClient
    {
        public override AsyncUnaryCall<UserReply> GetMeAsync(GetMeRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            return new AsyncUnaryCall<UserReply>(Task.FromResult(new UserReply
            {
                IsSuccess = true,
                StatusCode = 200,
                Result = new UserInfo
                {
                    UserId = user.ToString(),
                    RoleId = role,
                    IsActive = true
                }
            }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
            });
        }
    }

    private sealed class ScopeClient(CurrentRestaurantScopeReply reply) : RestaurantAccessGrpc.RestaurantAccessGrpcClient
    {
        public string? RequestedRestaurant { get; private set; }
        public string? Authorization { get; private set; }

        public override AsyncUnaryCall<CurrentRestaurantScopeReply> GetCurrentScopeAsync(
            CurrentRestaurantScopeRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            RequestedRestaurant = request.RestaurantId;
            Authorization = headers?.GetValue("authorization");
            return new AsyncUnaryCall<CurrentRestaurantScopeReply>(Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }
    }
}
