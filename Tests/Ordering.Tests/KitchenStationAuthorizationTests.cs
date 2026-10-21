using Backend.Contracts.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Ordering.API.Clients;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Xunit;

namespace Ordering.Tests;

public sealed class KitchenStationAuthorizationTests
{
    [Theory]
    [InlineData("manager", true, 200)]
    [InlineData("manager", false, 200)]
    [InlineData("kitchen", false, 200)]
    [InlineData("kitchen", true, 403)]
    [InlineData("admin", true, 403)]
    [InlineData("admin", false, 403)]
    [InlineData("waiter", false, 403)]
    [InlineData("cashier", true, 403)]
    [InlineData("inventory", true, 403)]
    [InlineData("Manager", true, 403)]
    [InlineData("", false, 401)]
    [InlineData("revoked", true, 401)]
    [InlineData("inactive", false, 401)]
    public async Task ConfiguredRolesAreEnforcedForLocalAndDocker(string role, bool write, int status)
    {
        foreach (var file in new[] { "station-local.json", "station-docker.json" })
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory).AddJsonFile(file).Build();
            var roles = configuration.GetSection("KitchenStationManagement").Get<KitchenStationSettings>()!;
            var http = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(role))
            {
                http.Request.Headers.Authorization = "Bearer " + role;
            }
            var access = new IdentityKitchenStationAccess(new IdentityClient(),
                new HttpContextAccessor
                {
                    HttpContext = http
                }, Options.Create(roles), Options.Create(new OrderingSettings()));
            if (status == 200)
            {
                await access.RequireAsync(write, default);
            }
            else
            {
                var error = await Assert.ThrowsAsync<OrderingException>(() => access.RequireAsync(write, default));
                Assert.Equal(status, error.StatusCode);
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
