extern alias restaurantapi;

using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Restaurant.Common.DTOs;
using Xunit;
using Client = restaurantapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient;
using Request = restaurantapi::Backend.Contracts.Identity.StaffProfilesRequest;
using Reply = restaurantapi::Backend.Contracts.Identity.UserListReply;
using Profile = restaurantapi::Backend.Contracts.Identity.UserInfo;
using ProfilesClient = restaurantapi::Restaurant.API.Management.StaffProfilesClient;

namespace Restaurant.Tests;

public sealed class StaffProfilesClientTests
{
    [Fact]
    public async Task LoadsProfileFieldsInBoundedBatchesIncludingInactiveAssignments()
    {
        var client = new Profiles();
        var service = new ProfilesClient(client, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["StaffProvisioning:ServiceKey"] = new string('k', 40) }).Build());
        var restaurant = Guid.NewGuid();
        var assignments = Enumerable.Range(0, 101).Select(index => new RestaurantStaffDto
        {
            RestaurantId = restaurant,
            UserIdRef = Guid.NewGuid(),
            IsActive = index % 2 == 0
        }).ToArray();
        await service.FillAsync(restaurant, assignments, default);
        Assert.Equal(2, client.Calls);
        Assert.All(assignments, item =>
        {
            Assert.Equal("Nguyen Van A", item.FullName);
            Assert.Equal("staff", item.UserName);
            Assert.Equal("staff@test.local", item.Email);
            Assert.Equal("waiter", item.RoleId);
            Assert.Equal("https://test.local/avatar.png", item.AvatarUrl);
        });
    }

    private sealed class Profiles : Client
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<Reply> GetStaffProfilesAsync(Request request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.InRange(request.UserIds.Count, 1, 100);
            Assert.NotEmpty(headers!.GetValue("x-staff-provisioning-key")!);
            var reply = new Reply
            {
                StatusCode = 200,
                IsSuccess = true
            };
            reply.Items.AddRange(request.UserIds.Select(id => new Profile
            {
                UserId = id,
                FullName = "Nguyen Van A",
                UserName = "staff",
                Email = "staff@test.local",
                RoleId = "waiter",
                AvatarUrl = "https://test.local/avatar.png"
            }));
            return new AsyncUnaryCall<Reply>(Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }
}
