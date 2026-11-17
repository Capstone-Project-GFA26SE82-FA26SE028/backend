extern alias identityapi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend.Contracts.Notifications;
using Grpc.Core;
using Grpc.Net.Client;
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
using Shared.Common.Notifications;
using Xunit;
using IdentityProgram = identityapi::Program;

namespace Restaurant.Tests;

public sealed class NotificationGatewayTests : IAsyncLifetime
{
    private const string Secret = "Notification-test-signing-key-at-least-32-bytes";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid restaurant = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid waiter = Guid.NewGuid();
    private readonly Guid customer = Guid.NewGuid();
    private readonly Dictionary<Guid, string> tokens = [];
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
                ["Jwt:Issuer"] = "notification-tests",
                ["Jwt:Audience"] = "notification-tests",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:SessionHours"] = "1",
                ["Notifications:ServiceKey"] = Secret,
                ["StaffProvisioning:ServiceKey"] = ""
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.AddScoped(_ => new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection).Options));
                services.RemoveAll<INotificationRestaurantAccess>();
                services.RemoveAll<INotificationAudience>();
                var access = new Audience(restaurant, admin, waiter);
                services.AddSingleton<INotificationRestaurantAccess>(access);
                services.AddSingleton<INotificationAudience>(access);
            });
        });
        using (var scope = identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            foreach (var (id, role) in new[] { (admin, "admin"), (waiter, "waiter"), (customer, "customer") })
            {
                db.Roles.Add(new Role { RoleId = role, RoleName = role });
                db.Users.Add(new User
                {
                    UserId = id,
                    RoleId = role,
                    RestaurantId = role == "customer" ? null : restaurant,
                    UserName = role,
                    FullName = role,
                    Email = role + "@example.test",
                    PasswordHash = "test-only",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                var session = Guid.NewGuid();
                db.UserSessions.Add(new UserSession
                {
                    UserSessionId = session,
                    UserId = id,
                    RefreshTokenHash = session.ToString("N") + session.ToString("N"),
                    LoggedInAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                });
                tokens[id] = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("notification-tests", "notification-tests",
                    [new Claim("UserId", id.ToString()), new Claim("RoleId", role), new Claim("sid", session.ToString())],
                    expires: DateTime.UtcNow.AddMinutes(15),
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)));
            }
            await db.SaveChangesAsync();
        }
        gateway = new WebApplicationFactory<ApiGateway.ApiAssembly>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.ConfigureTestServices(services =>
            {
                services.AddGrpcClient<NotificationGrpc.NotificationGrpcClient>().ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
                services.AddGrpcClient<AnnouncementGrpc.AnnouncementGrpcClient>().ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
                services.AddGrpcClient<NotificationSettingGrpc.NotificationSettingGrpcClient>().ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
            });
        });
        http = gateway.CreateClient();
    }

    private void Login(Guid id)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens[id]);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("result").Clone();
    }

    [Fact]
    public async Task AnnouncementAndNotificationApisWorkWithRealAuthenticationAndPrivateReadState()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/notifications")).StatusCode);
        Login(admin);
        var root = $"/api/restaurants/{restaurant}";
        var body = new AnnouncementWrite
        {
            Title = "Meeting",
            Message = "Shift meeting",
            RecipientRoleIds = ["waiter"]
        };
        var draft = await Result(await http.PostAsJsonAsync(root + "/announcements", body), HttpStatusCode.Created);
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetGuid();
        body.Version = version;
        body.Message = "Updated shift meeting";
        draft = await Result(await http.PutAsJsonAsync(root + $"/announcements/{id}", body));
        version = draft.GetProperty("version").GetGuid();
        await Result(await http.GetAsync(root + "/announcements"));
        await Result(await http.GetAsync(root + $"/announcements/{id}"));
        var sent = await Result(await http.PostAsJsonAsync(root + $"/announcements/{id}/send", new { version }));
        await Result(await http.PostAsJsonAsync(root + $"/announcements/{id}/send", new { version }));
        var notificationId = sent.GetProperty("notificationId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await http.DeleteAsync(root + $"/announcements/{id}?version={version}")).StatusCode);
        await Result(await http.GetAsync(root + "/notification-settings"));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PatchAsJsonAsync(root + "/notification-settings/TakeawayReady", new { enabled = false })).StatusCode);
        await Result(await http.PatchAsJsonAsync(root + "/notification-settings/Announcement", new { enabled = false }));
        await Result(await http.GetAsync(root + "/notifications"));
        await Result(await http.GetAsync(root + $"/notifications/{notificationId}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/restaurants/{Guid.NewGuid()}/notifications")).StatusCode);
        Login(waiter);
        Assert.Equal(1, (await Result(await http.GetAsync("/api/notifications/unread-count"))).GetProperty("count").GetInt32());
        await Result(await http.GetAsync("/api/notifications?isRead=false"));
        await Result(await http.GetAsync($"/api/notifications/{notificationId}"));
        await Result(await http.PatchAsync($"/api/notifications/{notificationId}/read", null));
        await Result(await http.PatchAsync("/api/notifications/read-all", null));
        Assert.Equal(0, (await Result(await http.GetAsync("/api/notifications/unread-count"))).GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(root + "/notifications")).StatusCode);
        Login(customer);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/notifications/{notificationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PatchAsync($"/api/notifications/{notificationId}/read", null)).StatusCode);
    }

    [Fact]
    public async Task PublishRejectsPublicCallerAndAcceptsOnlyInternalKeyWithoutDuplicating()
    {
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = identity.Server.CreateHandler()
        });
        var client = new NotificationGrpc.NotificationGrpcClient(channel);
        var request = new NotificationRequest
        {
            Json = JsonSerializer.Serialize(new NotificationEvent
            {
                Type = "TakeawayAccepted",
                EventKey = "Ordering:TakeawayAccepted:" + Guid.NewGuid(),
                RestaurantId = restaurant,
                EntityId = Guid.NewGuid(),
                CustomerId = customer,
                OccurredAt = DateTime.UtcNow
            })
        };
        Assert.Equal(403, (await client.PublishAsync(request)).StatusCode);
        Assert.Equal(403, (await client.PublishAsync(request, new Metadata { { "authorization", "Bearer " + tokens[admin] } })).StatusCode);
        var headers = new Metadata { { "x-notification-key", Secret } };
        Assert.Equal(200, (await client.PublishAsync(request, headers)).StatusCode);
        Assert.Equal(200, (await client.PublishAsync(request, headers)).StatusCode);
        Login(customer);
        Assert.Equal(1, (await Result(await http.GetAsync("/api/notifications/unread-count"))).GetProperty("count").GetInt32());
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await gateway.DisposeAsync();
        await identity.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class Audience(Guid restaurant, Guid admin, Guid waiter) : INotificationRestaurantAccess, INotificationAudience
    {
        public Task<bool> HasAssignmentAsync(Guid userId, Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult(restaurant == restaurantId && (userId == admin || userId == waiter));
        }

        public Task<IReadOnlySet<Guid>> GetStaffAsync(Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(restaurantId == restaurant ? new HashSet<Guid> { admin, waiter } : new HashSet<Guid>());
        }
    }
}
