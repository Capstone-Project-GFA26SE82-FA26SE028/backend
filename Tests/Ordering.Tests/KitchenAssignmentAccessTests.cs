using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Ordering.API.Clients;
using Ordering.Common.Exceptions;
using Shared.Persistence.RestaurantIsolation;
using Xunit;

namespace Ordering.Tests;

public sealed class KitchenAssignmentAccessTests
{
    [Theory]
    [InlineData("admin", false, true, true)]
    [InlineData("manager", false, true, true)]
    [InlineData("platform_admin", true, true, false)]
    [InlineData("kitchen", false, true, false)]
    [InlineData("admin", false, false, false)]
    public async Task ChecksManagerRoleAndRestaurant(string role, bool platform, bool assigned, bool allowed)
    {
        var scope = new RestaurantDataScope();
        scope.Select(Guid.NewGuid());
        var restaurants = new Restaurants(role, platform, assigned);
        var service = Create(scope, restaurants, new Profiles(), new Staff());
        if (allowed)
        {
            await service.RequireManager(default);
        }
        else
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<OrderingException>(() => service.RequireManager(default))).StatusCode);
        }
    }

    [Theory]
    [InlineData("kitchen", true, true, true)]
    [InlineData("waiter", true, true, false)]
    [InlineData("kitchen", false, true, false)]
    [InlineData("kitchen", true, false, false)]
    public async Task ChecksEmployeeRoleStatusAndActualRestaurant(string role, bool active, bool sameRestaurant, bool allowed)
    {
        var scope = new RestaurantDataScope();
        scope.Select(Guid.NewGuid());
        var staff = new Staff();
        var profiles = new Profiles
        {
            Role = role,
            Active = active,
            SameRestaurant = sameRestaurant
        };
        var service = Create(scope, new Restaurants("admin", false, true), profiles, staff);
        if (allowed)
        {
            await service.RequireEmployee(staff.UserId, default);
        }
        else
        {
            Assert.Equal(409, (await Assert.ThrowsAsync<OrderingException>(() => service.RequireEmployee(staff.UserId, default))).StatusCode);
        }
    }

    private static KitchenAssignmentAccess Create(RestaurantDataScope scope, Restaurants restaurants, Profiles profiles, Staff staff)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:ServiceKey"] = new string('n', 40),
            ["StaffProvisioning:ServiceKey"] = new string('s', 40)
        }).Build();
        return new KitchenAssignmentAccess(profiles, restaurants, staff, scope,
            new HttpContextAccessor { HttpContext = http }, configuration);
    }

    private static AsyncUnaryCall<T> Reply<T>(T value)
    {
        return new AsyncUnaryCall<T>(Task.FromResult(value), Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
            });
    }

    private sealed class Restaurants(string role, bool platform, bool assigned) : RestaurantAccessGrpc.RestaurantAccessGrpcClient
    {
        public override AsyncUnaryCall<CurrentRestaurantScopeReply> GetCurrentScopeAsync(CurrentRestaurantScopeRequest request,
            Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var result = new CurrentRestaurantScopeReply
            {
                RoleId = role,
                IsAdministrator = platform
            };
            if (assigned)
            {
                result.RestaurantIds.Add(request.RestaurantId);
            }

            return Reply(result);
        }
    }

    private sealed class Staff : RestaurantStaffGrpc.RestaurantStaffGrpcClient
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public override AsyncUnaryCall<RestaurantStaffListReply> GetNotificationRecipientsAsync(RestaurantStaffListRequest request,
            Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var result = new RestaurantStaffListReply();
            result.Items.Add(new RestaurantStaffInfo
            {
                UserIdRef = UserId.ToString(),
                RestaurantId = request.RestaurantId,
                IsActive = true
            });
            return Reply(result);
        }
    }

    private sealed class Profiles : UserGrpc.UserGrpcClient
    {
        public string Role { get; set; } = "kitchen";
        public bool Active { get; set; } = true;
        public bool SameRestaurant { get; set; } = true;

        public override AsyncUnaryCall<UserListReply> GetStaffProfilesAsync(StaffProfilesRequest request,
            Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var result = new UserListReply
            {
                IsSuccess = true
            };
            result.Items.AddRange(request.UserIds.Select(id => new UserInfo
            {
                UserId = id,
                RestaurantId = SameRestaurant ? request.RestaurantId : Guid.NewGuid().ToString(),
                RoleId = Role,
                IsActive = Active
            }));
            return Reply(result);
        }
    }
}
