extern alias restaurantapi;

using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Restaurant.Common.Exceptions;
using Xunit;
using AccessClient = restaurantapi::Backend.Contracts.Identity.AccessGrpc.AccessGrpcClient;
using AccessReply = restaurantapi::Backend.Contracts.Identity.AdminAccessReply;
using AccessRequest = restaurantapi::Backend.Contracts.Identity.GetMeRequest;
using ManagementAccess = restaurantapi::Restaurant.API.Management.IdentityManagementAccess;

namespace Restaurant.Tests;

public sealed class IdentityAccessTests
{
    [Fact]
    public async Task MissingTokenNeverCallsIdentity()
    {
        var client = new FakeAccessClient();
        var access = new ManagementAccess(client, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        });
        var error = await Assert.ThrowsAsync<ManagementException>(() => access.RequireAdmin(default));
        Assert.Equal(401, error.StatusCode);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task TokenIsForwardedAndIdentityActorIsUsed()
    {
        var client = new FakeAccessClient();
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer opaque-token";
        var access = new ManagementAccess(client, new HttpContextAccessor
        {
            HttpContext = http
        });
        Assert.Equal(client.Actor, await access.RequireAdmin(default));
        Assert.Equal("Bearer opaque-token", client.Authorization);
        Assert.NotNull(client.Deadline);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unavailable)]
    public async Task IdentityRejectionOrOutageNeverGrantsAccess(StatusCode status)
    {
        var client = new FakeAccessClient
        {
            Failure = status
        };
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer rejected-token";
        var access = new ManagementAccess(client, new HttpContextAccessor
        {
            HttpContext = http
        });
        var error = await Assert.ThrowsAsync<RpcException>(() => access.RequireAdmin(default));
        Assert.Equal(status, error.StatusCode);
    }

    private sealed class FakeAccessClient : AccessClient
    {
        public Guid Actor { get; } = Guid.NewGuid();
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        public DateTime? Deadline { get; private set; }
        public StatusCode? Failure { get; set; }

        public override AsyncUnaryCall<AccessReply> RequireAdminAsync(AccessRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Authorization = headers?.GetValue("authorization");
            Deadline = deadline;
            var task = Failure.HasValue
                ? Task.FromException<AccessReply>(new RpcException(new Status(Failure.Value, "Rejected")))
                : Task.FromResult(new AccessReply
                {
                    UserId = Actor.ToString()
                });
            return new AsyncUnaryCall<AccessReply>(task, Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }
    }
}
