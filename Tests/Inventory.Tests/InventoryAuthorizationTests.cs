using Backend.Contracts.Identity;
using Grpc.Core;
using Inventory.API.Clients;
using Inventory.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Inventory.Tests;

public sealed class InventoryAuthorizationTests
{
    [Theory]
    [InlineData("manager", "ManageIngredients", 200)]
    [InlineData("manager", "Read", 200)]
    [InlineData("manager", "Receive", 403)]
    [InlineData("inventory", "Receive", 200)]
    [InlineData("inventory", "Issue", 200)]
    [InlineData("manager", "Issue", 403)]
    [InlineData("kitchen", "Issue", 403)]
    [InlineData("admin", "Issue", 403)]
    [InlineData("inventory", "Read", 200)]
    [InlineData("inventory", "ManageIngredients", 403)]
    [InlineData("admin", "ManageIngredients", 403)]
    [InlineData("waiter", "Read", 403)]
    [InlineData("kitchen", "Receive", 403)]
    [InlineData("cashier", "Read", 403)]
    [InlineData("Manager", "ManageIngredients", 403)]
    [InlineData("", "Read", 401)]
    [InlineData("revoked", "Read", 401)]
    [InlineData("inactive", "Read", 401)]
    public async Task RolesAndSessionsAreEnforced(string role, string operation, int status)
    {
        foreach (var file in new[] { "inventory-local.json", "inventory-docker.json" })
        {
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(file).Build();
            var http = new DefaultHttpContext();
            if (role.Length > 0)
            {
                http.Request.Headers.Authorization = "Bearer " + role;
            }
            var access = new IdentityInventoryAccess(new IdentityClient(), new HttpContextAccessor
            {
                HttpContext = http
            }, configuration);
            if (status == 200)
            {
                Assert.NotEqual(Guid.Empty, await access.RequireAsync(operation, default));
            }
            else
            {
                Assert.Equal(status, (await Assert.ThrowsAsync<InventoryException>(() => access.RequireAsync(operation, default))).StatusCode);
            }
        }
    }

    private sealed class IdentityClient : UserGrpc.UserGrpcClient
    {
        public override AsyncUnaryCall<UserReply> GetMeAsync(GetMeRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var role = headers?.GetValue("authorization")?.Replace("Bearer ", "") ?? "";
            var reply = new UserReply
            {
                IsSuccess = role != "revoked",
                StatusCode = role == "revoked" ? 401 : 200,
                Result = new UserInfo
                {
                    UserId = "11111111-1111-1111-1111-111111111111",
                    RoleId = role,
                    IsActive = role != "inactive"
                }
            };
            return new AsyncUnaryCall<UserReply>(Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }
    }
}
