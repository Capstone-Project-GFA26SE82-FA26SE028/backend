extern alias restaurantapi;

using System.Net;
using System.Net.Http.Json;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurant.BLL.Interfaces;
using ApiGateway.DTOs.Restaurant;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;
using Shared.Common.DTOs;
using Xunit;
using RestaurantMarker = restaurantapi::Restaurant.API.ApiAssembly;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestQrImageStore _qrImages = new();
    private WebApplicationFactory<RestaurantMarker> _restaurant = null!;
    private WebApplicationFactory<ApiGateway.ApiAssembly> _gateway = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _restaurant = new WebApplicationFactory<RestaurantMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // Program reads these before ConfigureAppConfiguration callbacks run.
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Qr:MenuBaseUrl"] = "https://menu.example.test/menu",
                    ["Reservations:ExpirationEnabled"] = "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<RestaurantDbContext>();
                services.RemoveAll<IQrImageStore>();
                services.AddSingleton<IQrImageStore>(_qrImages);
                services.AddScoped<RestaurantDbContext>(_ => new TestDb(_connection));
                services.AddSingleton<TimeProvider>(_reservationClock);
                services.RemoveAll<IPaymentRequestOrderClient>();
                services.AddSingleton<IPaymentRequestOrderClient>(new PaymentRequestOrders(() => _pendingPaymentOrders));
                services.RemoveAll<IManagementAccess>();
                services.AddScoped<IManagementAccess, TestAccess>();
                services.RemoveAll<IStaffIdentity>();
                services.AddScoped<IStaffIdentity, TestStaffIdentity>();
                services.RemoveAll<restaurantapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient>();
                services.AddSingleton<restaurantapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient, WaiterIdentityClient>();
            });
        });
        using (var scope = _restaurant.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Database.EnsureCreatedAsync();
        }

        _gateway = new WebApplicationFactory<ApiGateway.ApiAssembly>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.UseSetting("GrpcServices:Restaurant", "http://restaurant.test");
            builder.ConfigureTestServices(services =>
            {
                services.AddGrpcClient<RestaurantGrpc.RestaurantGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<AreaGrpc.AreaGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<TableGrpc.TableGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<QrGrpc.QrGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<GuestAccessGrpc.GuestAccessGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<RestaurantStaffGrpc.RestaurantStaffGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<ReservationGrpc.ReservationGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<TableSessionGrpc.TableSessionGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<ServiceRequestGrpc.ServiceRequestGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
            });
        });
        _http = _gateway.CreateClient();
        _http.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
    }

    [Fact]
    public void ReservationExpirationWorkerUsesTheBoundConfiguration()
    {
        var workers = _restaurant.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        Assert.Single(workers.OfType<restaurantapi::Restaurant.API.Reservations.ReservationExpirationWorker>());
        var settings = _restaurant.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Restaurant.Common.Settings.ReservationSettings>>().Value;
        Assert.False(settings.ExpirationEnabled);
        Assert.Equal(120, settings.DefaultDurationMinutes);
        Assert.Equal(30, settings.HoldBeforeMinutes);
        Assert.Equal(30, settings.HoldAfterMinutes);
    }

    [Fact]
    public async Task AdminCanManageRestaurantAreasAndTablesThroughGateway()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "A");
        var target = await CreateArea(restaurant.RestaurantId, "B");
        var table = await CreateTable(area.AreaId);
        var list = await _http.GetFromJsonAsync<ResponseDTO<PageResult<TableDto>>>(
            $"/api/tables?restaurantId={restaurant.RestaurantId}&search=T1&page=1&pageSize=10");
        Assert.Equal(table.TableId, Assert.Single(list!.Result!.Items).TableId);

        Assert.Equal(HttpStatusCode.Conflict, (await _http.DeleteAsync($"/api/areas/{area.AreaId}")).StatusCode);
        var moved = await Read<TableDto>(await _http.PatchAsJsonAsync($"/api/tables/{table.TableId}/area", new AssignAreaDto
        {
            AreaId = target.AreaId,
            Version = table.Version
        }));
        Assert.Equal(target.AreaId, moved.AreaId);
        Assert.NotEqual(table.Version, moved.Version);
        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/areas/{area.AreaId}")).StatusCode);

        var disabled = await Read<TableDto>(await _http.PatchAsJsonAsync($"/api/tables/{table.TableId}/status", new StatusDto
        {
            IsActive = false,
            Version = moved.Version
        }));
        Assert.False(disabled.IsActive);
        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync(
            $"/api/tables/{table.TableId}?version={Uri.EscapeDataString(disabled.Version)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/tables/{table.TableId}")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    public async Task DeniedActorCannotReadOrWrite(string? token, HttpStatusCode expected)
    {
        _http.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        Assert.Equal(expected, (await _http.GetAsync("/api/restaurants")).StatusCode);
        Assert.Equal(expected, (await _http.PostAsJsonAsync("/api/restaurants", RestaurantBody())).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Restaurants.CountAsync());
    }

    [Fact]
    public async Task DuplicateCodesAndStaleVersionAreRejected()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "A");
        var duplicate = await _http.PostAsJsonAsync("/api/areas", AreaBody(restaurant.RestaurantId, " a "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var table = await CreateTable(area.AreaId);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = area.AreaId,
            TableCode = "t1",
            Capacity = 4
        })).StatusCode);
        var updated = await Read<TableDto>(await _http.PutAsJsonAsync($"/api/tables/{table.TableId}", new TableWriteDto
        {
            AreaId = area.AreaId,
            TableCode = "T1",
            Capacity = 8,
            Version = table.Version
        }));
        Assert.Equal(8, updated.Capacity);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PatchAsJsonAsync($"/api/tables/{table.TableId}/status", new StatusDto
        {
            IsActive = false,
            Version = table.Version
        })).StatusCode);
    }

    [Fact]
    public async Task ActiveSessionBlocksMovingDisablingAndDeletion()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "A");
        var target = await CreateArea(restaurant.RestaurantId, "B");
        var table = await CreateTable(area.AreaId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.SessionTables.Add(new SessionTable
            {
                SessionTableId = Guid.NewGuid(),
                TableId = table.TableId,
                AssignedAt = DateTime.UtcNow,
                TableSession = new TableSession
                {
                    TableSessionId = Guid.NewGuid(),
                    PartySize = 2,
                    Status = "Open",
                    OpenedAt = DateTime.UtcNow
                }
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _http.PatchAsJsonAsync($"/api/tables/{table.TableId}/area", new AssignAreaDto
        {
            AreaId = target.AreaId,
            Version = table.Version
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PatchAsJsonAsync($"/api/tables/{table.TableId}/status", new StatusDto
        {
            IsActive = false,
            Version = table.Version
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PatchAsJsonAsync($"/api/areas/{area.AreaId}/status", new StatusDto
        {
            IsActive = false
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.DeleteAsync(
            $"/api/tables/{table.TableId}?version={Uri.EscapeDataString(table.Version)}")).StatusCode);
    }

    [Fact]
    public async Task InvalidHoursAndCapacityDoNotPersist()
    {
        var invalid = RestaurantBody();
        invalid.OpeningHours[0].CloseAt = "07:00";
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/restaurants", invalid)).StatusCode);
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "A");
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = area.AreaId,
            TableCode = "T0",
            Capacity = 0
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/areas?pageSize=101")).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Restaurants.CountAsync());
    }

    [Fact]
    public async Task InactiveAreaRejectsNewTableAndRestaurantUpdateReplacesHours()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "A");
        await Read<AreaDto>(await _http.PatchAsJsonAsync($"/api/areas/{area.AreaId}/status", new StatusDto
        {
            IsActive = false
        }));
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = area.AreaId,
            TableCode = "T1",
            Capacity = 4
        })).StatusCode);
        var body = RestaurantBody();
        body.OpeningHours[0].OpenAt = "09:00";
        var updated = await Read<RestaurantDto>(await _http.PutAsJsonAsync($"/api/restaurants/{restaurant.RestaurantId}", body));
        Assert.Equal("09:00", Assert.Single(updated.OpeningHours).OpenAt);
        using var scope = _restaurant.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().OpeningHours.CountAsync());
    }

    private async Task<RestaurantDto> CreateRestaurant()
    {
        return await Read<RestaurantDto>(await _http.PostAsJsonAsync("/api/restaurants", RestaurantBody()), HttpStatusCode.Created);
    }

    private async Task<AreaDto> CreateArea(Guid restaurantId, string code)
    {
        return await Read<AreaDto>(await _http.PostAsJsonAsync("/api/areas", AreaBody(restaurantId, code)), HttpStatusCode.Created);
    }

    private async Task<TableDto> CreateTable(Guid areaId)
    {
        return await Read<TableDto>(await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = areaId,
            TableCode = "T1",
            Capacity = 4
        }), HttpStatusCode.Created);
    }

    private static RestaurantWriteDto RestaurantBody()
    {
        return new RestaurantWriteDto
        {
            RestaurantName = "Nhà hàng thử nghiệm",
            Address = "Địa chỉ",
            Hotline = "0901234567",
            OpeningHours =
            [
                new OpeningHourDto
                {
                    DayOfWeek = 1,
                    OpenAt = "08:00",
                    CloseAt = "22:00"
                }
            ]
        };
    }

    private static AreaWriteDto AreaBody(Guid restaurantId, string code)
    {
        return new AreaWriteDto
        {
            RestaurantId = restaurantId,
            AreaCode = code,
            AreaName = code,
            ServiceType = "DineIn",
            SpaceType = "Indoor"
        };
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {content}");
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDTO<T>>();
        Assert.True(envelope!.IsSuccess);
        return envelope.Result!;
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_gateway is not null)
        {
            await _gateway.DisposeAsync();
        }

        if (_restaurant is not null)
        {
            await _restaurant.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    private sealed class TestStaffIdentity(IHttpContextAccessor accessor) : IStaffIdentity
    {
        public Task<Restaurant.Common.DTOs.StaffActorDto> GetUserAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(new Restaurant.Common.DTOs.StaffActorDto
            {
                UserId = userId,
                RoleId = "waiter"
            });
        }

        public Task<Restaurant.Common.DTOs.StaffActorDto> GetCurrentAsync(CancellationToken ct)
        {
            var token = accessor.HttpContext!.Request.Headers.Authorization.ToString();
            if (token is "" or "Bearer revoked")
            {
                throw new ManagementException(401, "Invalid session.");
            }

            return Task.FromResult(new Restaurant.Common.DTOs.StaffActorDto
            {
                UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RoleId = token.Replace("Bearer ", string.Empty),
                IsAdministrator = token == "Bearer admin"
            });
        }
    }

    private sealed class TestAccess(IHttpContextAccessor accessor) : IManagementAccess
    {
        public Task<Guid> RequireAdmin(CancellationToken ct)
        {
            var auth = accessor.HttpContext!.Request.Headers.Authorization.ToString();
            if (auth is "" or "Bearer revoked")
            {
                throw new ManagementException(401, "Invalid session.");
            }

            if (auth != "Bearer admin")
            {
                throw new ManagementException(403, "Admin required.");
            }

            return Task.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        }
    }

    private sealed class TestDb(SqliteConnection connection) : RestaurantDbContext(
        new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Table>().Property(x => x.Version).ValueGeneratedNever();
        }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (var entry in ChangeTracker.Entries<Table>()
                .Where(x => x.State is EntityState.Added or EntityState.Modified))
            {
                entry.Entity.Version = Guid.NewGuid().ToByteArray();
            }

            return base.SaveChangesAsync(ct);
        }
    }
}
