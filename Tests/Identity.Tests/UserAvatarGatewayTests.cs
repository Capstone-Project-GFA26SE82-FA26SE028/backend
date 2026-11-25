extern alias identityapi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend.Contracts.Identity;
using Identity.BLL.Interfaces;
using Identity.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Shared.Common.Images;
using Xunit;
using IdentityProgram = identityapi::Program;

namespace Identity.Tests;

public sealed class UserAvatarGatewayTests : IAsyncLifetime
{
    private const string Secret = "avatar-test-only-signing-key-at-least-32-characters";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid restaurantId = Guid.NewGuid();
    private readonly Dictionary<string, (Guid UserId, string Token)> accounts = [];
    private readonly CloudStorage storage = new();
    private WebApplicationFactory<IdentityProgram> identity = null!;
    private WebApplicationFactory<ApiGateway.ApiAssembly> gateway = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        identity = new WebApplicationFactory<IdentityProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = Secret,
                ["Jwt:Issuer"] = "avatar-tests",
                ["Jwt:Audience"] = "avatar-tests",
                ["StaffProvisioning:ServiceKey"] = ""
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.AddScoped(_ => new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseSqlite(connection).Options));
                services.RemoveAll<IImageStorage>();
                services.AddSingleton<IImageStorage>(storage);
                services.RemoveAll<IUserRestaurantAccess>();
                services.AddSingleton<IUserRestaurantAccess>(new RestaurantAccess());
            });
        });
        using (var scope = identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            foreach (var role in new[] { "platform_admin", "admin", "waiter", "customer" })
            {
                db.Roles.Add(new Role
                {
                    RoleId = role,
                    RoleName = role
                });
            }

            foreach (var name in new[] { "platform_admin", "admin", "waiter", "foreign", "customer" })
            {
                var userId = Guid.NewGuid();
                var role = name == "foreign" ? "waiter" : name;
                var sessionId = Guid.NewGuid();
                db.Users.Add(new User
                {
                    UserId = userId,
                    RoleId = role,
                    RestaurantId = name == "foreign" ? Guid.NewGuid() : role is "admin" or "waiter" ? restaurantId : null,
                    UserName = name,
                    FullName = name,
                    Email = name + "@example.test",
                    PasswordHash = "test-only",
                    AvatarUrl = "https://legacy.test/old.png",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                db.UserSessions.Add(new UserSession
                {
                    UserSessionId = sessionId,
                    UserId = userId,
                    RefreshTokenHash = sessionId.ToString("N") + sessionId.ToString("N"),
                    LoggedInAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                });
                var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("avatar-tests", "avatar-tests",
                    [new Claim("UserId", userId.ToString()), new Claim("RoleId", role), new Claim("sid", sessionId.ToString())],
                    expires: DateTime.UtcNow.AddMinutes(15),
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                        SecurityAlgorithms.HmacSha256)));
                accounts[name] = (userId, token);
            }

            await db.SaveChangesAsync();
        }

        gateway = new WebApplicationFactory<ApiGateway.ApiAssembly>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.ConfigureTestServices(services => services.AddGrpcClient<UserGrpc.UserGrpcClient>()
                .ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler()));
        });
        http = gateway.CreateClient();
    }

    [Theory]
    [InlineData("customer", "me", 200)]
    [InlineData("waiter", "me", 200)]
    [InlineData("admin", "waiter", 200)]
    [InlineData("admin", "foreign", 403)]
    [InlineData("waiter", "foreign", 403)]
    [InlineData("platform_admin", "foreign", 200)]
    public async Task GatewayUploadEnforcesSelfAndExistingRestaurantManagementScope(string actor, string target, int status)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts[actor].Token);
        var route = target == "me" ? "me" : accounts[target].UserId.ToString();
        using var response = await Upload(route, ValidPng());
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = target == "me" ? accounts[actor].UserId : accounts[target].UserId;
        var saved = await db.Users.SingleAsync(user => user.UserId == userId);
        Assert.Equal(status == 200 ? CloudStorage.Url : "https://legacy.test/old.png", saved.AvatarUrl);
        Assert.Equal(status == 200 ? 1 : 0, storage.Calls);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (status == 200)
        {
            Assert.Equal(CloudStorage.Url, json.RootElement.GetProperty("result").GetProperty("avatarUrl").GetString());
            Assert.Equal(userId.ToString(), json.RootElement.GetProperty("result").GetProperty("userId").GetString());
        }
    }

    [Fact]
    public async Task GatewayRejectsAnonymousUploadWithoutCallingCloud()
    {
        using var response = await Upload("me", ValidPng());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task RevokedSessionCannotUploadAvatar()
    {
        using (var scope = identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.UserSessions.Where(session => session.UserId == accounts["customer"].UserId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, DateTime.UtcNow));
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts["customer"].Token);
        using var response = await Upload("me", ValidPng());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task MissingFileIsRejected()
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts["customer"].Token);
        using var form = new MultipartFormDataContent();
        using var response = await http.PostAsync("/api/users/me/avatar", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(2097153)]
    public async Task GatewayRejectsInvalidOrOversizeImageWithoutCallingCloud(int size)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts["customer"].Token);
        using var response = await Upload("me", new byte[size]);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task GatewayCloudFailureDoesNotChangeAvatar()
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts["customer"].Token);
        storage.Fail = true;
        using var response = await Upload("me", ValidPng());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal("https://legacy.test/old.png", (await db.Users.SingleAsync(user => user.UserId == accounts["customer"].UserId)).AvatarUrl);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    private async Task<HttpResponseMessage> Upload(string user, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "avatar.png");
        return await http.PostAsync($"/api/users/{user}/avatar", content);
    }

    private static byte[] ValidPng()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8ioAAAAASUVORK5CYII=");
    }

    public async Task DisposeAsync()
    {
        http?.Dispose();
        if (gateway is not null)
        {
            await gateway.DisposeAsync();
        }

        if (identity is not null)
        {
            await identity.DisposeAsync();
        }

        await connection.DisposeAsync();
    }

    private sealed class RestaurantAccess : IUserRestaurantAccess
    {
        public Task<bool> CanManageAsync(Guid actorId, Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class CloudStorage : IImageStorage
    {
        public const string Url = "https://res.cloudinary.com/test/image/upload/identity/avatars/uploaded.png";
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
        {
            Calls++;
            if (Fail)
            {
                throw new ImageStorageException(503, "Cloudinary temporarily unavailable.");
            }

            return Task.FromResult(Url);
        }

        public Task<byte[]> ReadAsync(string url, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }
}
