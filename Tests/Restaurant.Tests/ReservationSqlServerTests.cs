extern alias identityapi;
extern alias restaurantapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Grpc.Net.Client;
using Identity.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Restaurant.BLL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Common.DTOs;
using Xunit;
using IdentityMarker = identityapi::Program;
using RestaurantIdentityUser = restaurantapi::Backend.Contracts.Identity.UserGrpc.UserGrpcClient;
using RestaurantIdentityAccess = restaurantapi::Backend.Contracts.Identity.AccessGrpc.AccessGrpcClient;
using RestaurantMarker = restaurantapi::Restaurant.API.ApiAssembly;

namespace Restaurant.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTAURANT_SQL_TEST_SERVER")))
        {
            Skip = "Set RESTAURANT_SQL_TEST_SERVER to an isolated SQL Server instance to run real database tests.";
        }
    }
}

public sealed partial class ReservationSqlServerTests : IAsyncLifetime
{
    private readonly SqlClock _clock = new();
    private readonly ReservationWriteBarrier _writeBarrier = new();
    private readonly ReservationWriteFault _writeFault = new();
    private readonly Dictionary<string, Guid> _users = new();
    private readonly Dictionary<string, string> _tokens = new();
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)) + "aA1!";
    private WebApplicationFactory<IdentityMarker> _identity = null!;
    private WebApplicationFactory<RestaurantMarker> _restaurant = null!;
    private WebApplicationFactory<ApiGateway.ApiAssembly> _gateway = null!;
    private string _identityConnection = string.Empty;
    private string _restaurantConnection = string.Empty;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("RESTAURANT_SQL_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        _identityConnection = Connection(server, "CodexReservationIdentity_" + suffix);
        _restaurantConnection = Connection(server, "CodexReservationRestaurant_" + suffix);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        _identity = new WebApplicationFactory<IdentityMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _identityConnection);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["Jwt:SecretKey"] = key,
                ["Jwt:Issuer"] = "https://identity.sql.test",
                ["Jwt:Audience"] = "https://gateway.sql.test",
                ["UserManagement:AdminRoleIds:0"] = "admin"
            }));
        });
        using (var scope = _identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            foreach (var role in new[] { "admin", "waiter", "cashier", "kitchen" })
            {
                db.Roles.Add(new Role
                {
                    RoleId = role,
                    RoleName = role
                });
                var id = Guid.NewGuid();
                _users[role] = id;
                db.Users.Add(new User
                {
                    UserId = id,
                    RoleId = role,
                    UserName = role,
                    Email = role + "@sql.example.test",
                    FullName = "SQL Test " + role,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(_password),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        using (var channel = GrpcChannel.ForAddress("http://identity.test", new GrpcChannelOptions
        {
            HttpHandler = _identity.Server.CreateHandler()
        }))
        {
            var auth = new IdentityGrpc.IdentityGrpcClient(channel);
            foreach (var role in _users.Keys)
            {
                var result = await auth.LoginAsync(new LoginRequest
                {
                    Email = role + "@sql.example.test",
                    Password = _password
                });
                Assert.True(result.IsSuccess);
                _tokens[role] = result.Result.AccessToken;
            }
        }

        _restaurant = new WebApplicationFactory<RestaurantMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _restaurantConnection);
            builder.UseSetting("GrpcServices:Identity", "http://identity.test");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["Reservations:ExpirationEnabled"] = "false",
                ["Reservations:StaffRoleIds:0"] = "waiter",
                ["Reservations:StaffRoleIds:1"] = "cashier"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_clock);
                services.RemoveAll<DbContextOptions<RestaurantDbContext>>();
                services.RemoveAll<RestaurantDbContext>();
                services.AddDbContext<RestaurantDbContext>(options => options.UseSqlServer(_restaurantConnection)
                    .AddInterceptors(_writeBarrier, _writeFault));
                services.AddGrpcClient<RestaurantIdentityUser>()
                    .ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler());
                services.AddGrpcClient<RestaurantIdentityAccess>()
                    .ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler());
            });
        });
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");
        }

        _gateway = new WebApplicationFactory<ApiGateway.ApiAssembly>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Swagger:Enabled", "true");
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
                services.AddGrpcClient<RestaurantStaffGrpc.RestaurantStaffGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
                services.AddGrpcClient<ReservationGrpc.ReservationGrpcClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
            });
        });
        _http = _gateway.CreateClient();
    }

    [SqlServerFact]
    public async Task GatewayFlowUsesRealIdentityAndSqlServerWithoutOpeningASession()
    {
        var location = await Location();
        var pending = await Pending(location);
        var visible = await Read<PageResult<ReservationDto>>(await Send(HttpMethod.Get, "/api/reservations", "waiter"));
        Assert.Equal(pending.ReservationId, Assert.Single(visible.Items).ReservationId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(pending, location.TableId, "admin")).StatusCode);
        var confirmed = await Read<ReservationDto>(await Confirm(pending, location.TableId));
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(_users["waiter"], confirmed.ApprovedByRef);
        Assert.Equal(pending.EndAt, confirmed.EndAt);
        Assert.Equal(confirmed.StartAt.AddMinutes(30), confirmed.HoldUntil);
        Assert.Equal(location.TableId, Assert.Single(confirmed.AssignedTableIds));
        var read = await Read<ReservationDto>(await Send(HttpMethod.Get, $"/api/reservations/{pending.ReservationId}", "waiter"));
        Assert.Equal("Confirmed", read.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(pending, location.TableId)).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ReservationTable").SingleAsync());
        Assert.Empty(await db.TableSessions.ToListAsync());
        Assert.Empty(await db.GuestAccesses.ToListAsync());
        Assert.Equal("Confirmed", (await Read<ReservationDto>(await Send(HttpMethod.Get,
            $"/api/reservations/{pending.ReservationId}", "admin"))).Status);
    }

    [SqlServerFact]
    public async Task WaiterAndCashierCanAccessMultipleAssignedRestaurants()
    {
        var first = await Location();
        var second = await Location();
        var firstRequest = await Pending(first);
        var secondRequest = await Pending(second);
        foreach (var role in new[] { "waiter", "cashier" })
        {
            var list = await Read<PageResult<ReservationDto>>(await Send(HttpMethod.Get, "/api/reservations", role));
            Assert.Equal(2, list.TotalCount);
            await Read<ReservationDto>(await Send(HttpMethod.Get, $"/api/reservations/{secondRequest.ReservationId}", role));
            await Read<ReservationAvailabilityDto>(await Send(HttpMethod.Get,
                $"/api/reservations/availability?restaurantId={second.RestaurantId}&areaId={second.AreaId}&partySize=2&startAt=2030-01-07T10:00:00Z", role));
        }

        Assert.Equal(HttpStatusCode.OK, (await Confirm(firstRequest, first.TableId, "waiter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(secondRequest, second.TableId, "cashier")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get,
            $"/api/restaurants/{first.RestaurantId}/staff", "admin")).StatusCode);
    }

    [SqlServerFact]
    public async Task ApprovalDeadlineIsInclusiveAndArrivalDeadlineProducesNoShow()
    {
        var location = await Location();
        var early = await Pending(location, _clock.Now.AddMinutes(30));
        using (var boundaryScope = _restaurant.Services.CreateScope())
        {
            Assert.Equal(0, await boundaryScope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.OK, (await Confirm(early, location.TableId)).StatusCode);
        var late = await Pending(location, _clock.Now.AddHours(1));
        _clock.Now = _clock.Now.AddMinutes(31);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(late, location.TableId)).StatusCode);
        var read = await Read<ReservationDto>(await Send(HttpMethod.Get, $"/api/reservations/{late.ReservationId}", "waiter"));
        Assert.Equal("Expired", read.Status);
        Assert.Empty(read.AssignedTableIds);
        _clock.Now = _clock.Now.AddHours(5);
        using var scope = _restaurant.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(CancellationToken.None);
        var stored = await scope.ServiceProvider.GetRequiredService<RestaurantDbContext>().Reservations
            .SingleAsync(value => value.ReservationId == early.ReservationId);
        Assert.Equal("NoShow", stored.Status);
    }

    [SqlServerFact]
    public async Task CashierCanConfirmAndAnInactiveAccountCannotAccessReservations()
    {
        var location = await Location();
        var pending = await Pending(location);
        var confirmed = await Read<ReservationDto>(await Confirm(pending, location.TableId, "cashier"));
        Assert.Equal(_users["cashier"], confirmed.ApprovedByRef);
        using (var scope = _identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.SingleAsync(value => value.UserId == _users["cashier"])).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/reservations", "cashier")).StatusCode);
    }

    [SqlServerFact]
    public async Task ConfirmRechecksTheDeadlineAfterWaitingForTableLocks()
    {
        var location = await Location();
        var pending = await Pending(location, _clock.Now.AddHours(1));
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = new SqlCommand("SELECT TableId FROM [Table] WITH (UPDLOCK, HOLDLOCK) WHERE TableId = @id", blocker, transaction))
        {
            command.Parameters.AddWithValue("@id", location.TableId);
            await command.ExecuteScalarAsync();
        }

        var response = Confirm(pending, location.TableId);
        var observed = await WaitForBlocked(blocker.ServerProcessId, 1);
        _clock.Now = _clock.Now.AddMinutes(31);
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await response).StatusCode);
        Assert.True(observed);
        var read = await Read<ReservationDto>(await Send(HttpMethod.Get, $"/api/reservations/{pending.ReservationId}", "waiter"));
        Assert.Equal("Expired", read.Status);
        Assert.Empty(read.AssignedTableIds);
    }

    [SqlServerFact]
    public async Task TwoRequestsCannotAssignTheSameTableInOverlappingIntervals()
    {
        var location = await Location();
        var first = await Pending(location);
        var second = await Pending(location);
        var responses = await Contend(location.TableId, () => Confirm(first, location.TableId),
            () => Confirm(second, location.TableId, "cashier"));
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Conflict);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(1, await db.Reservations.CountAsync(value => value.Status == "Confirmed"));
        Assert.Equal(1, await db.Reservations.CountAsync(value => value.Status == "Pending"));
    }

    [SqlServerFact]
    public async Task TwoEmployeesCannotConfirmTheSameRequestTwice()
    {
        var location = await Location();
        var pending = await Pending(location);
        var responses = await Contend(location.TableId, () => Confirm(pending, location.TableId),
            () => Confirm(pending, location.TableId, "cashier"));
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Conflict);
    }

    [SqlServerFact]
    public async Task ExpirationAndConfirmCannotOverwriteEachOther()
    {
        var location = await Location();
        var pending = await Pending(location, _clock.Now.AddMinutes(30));
        _clock.Now = _clock.Now.AddSeconds(1);
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = new SqlCommand("SELECT ReservationId FROM Reservation WITH (UPDLOCK, HOLDLOCK) WHERE ReservationId = @id", blocker, transaction))
        {
            command.Parameters.AddWithValue("@id", pending.ReservationId);
            await command.ExecuteScalarAsync();
        }

        using var scope = _restaurant.Services.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<IReservationExpirationService>();
        var confirm = Confirm(pending, location.TableId);
        var expire = worker.Expire(CancellationToken.None);
        var observed = await WaitForBlocked(blocker.ServerProcessId, 2);
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await confirm).StatusCode);
        await expire;
        Assert.True(observed, "Confirm and expiration did not both wait on the reservation lock.");
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.ChangeTracker.Clear();
        var stored = await db.Reservations.Include(value => value.Tables).SingleAsync();
        Assert.Equal("Expired", stored.Status);
        Assert.Null(stored.ApprovedByRef);
        Assert.Empty(stored.Tables);
        Assert.Equal(0, await worker.Expire(CancellationToken.None));
    }

    [SqlServerFact]
    public async Task CurrentOccupancyDoesNotAutomaticallyBlockALaterReservation()
    {
        var location = await Location();
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var session = new TableSession
            {
                TableSessionId = Guid.NewGuid(),
                PartySize = 2,
                Status = "Open",
                OpenedAt = _clock.Now.UtcDateTime
            };
            db.SessionTables.Add(new SessionTable
            {
                SessionTableId = Guid.NewGuid(),
                TableSession = session,
                TableId = location.TableId,
                AssignedAt = _clock.Now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        var immediate = await Pending(location, _clock.Now.AddMinutes(30));
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(immediate, location.TableId)).StatusCode);
        var pending = await Pending(location);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(pending, location.TableId)).StatusCode);
    }

    [SqlServerFact]
    public async Task ConfirmChecksCapacityAreaActivityAndDoesNotPartiallyAssignTables()
    {
        var location = await Location();
        var other = await Location();
        var second = await Read<TableDto>(await Send(HttpMethod.Post, "/api/tables", "admin", new
        {
            areaId = location.AreaId,
            tableCode = "T2",
            capacity = 4
        }), HttpStatusCode.Created);
        var pending = await Pending(location, partySize: 6);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(pending, location.TableId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(pending, other.TableId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post,
            $"/api/reservations/{pending.ReservationId}/confirm", "waiter",
            new { tableIds = new[] { location.TableId, location.TableId } })).StatusCode);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.Tables.SingleAsync(value => value.TableId == second.TableId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post,
            $"/api/reservations/{pending.ReservationId}/confirm", "waiter",
            new { tableIds = new[] { location.TableId, second.TableId } })).StatusCode);
        var stillPending = await Read<ReservationDto>(await Send(HttpMethod.Get,
            $"/api/reservations/{pending.ReservationId}", "waiter"));
        Assert.Equal("Pending", stillPending.Status);
        Assert.Empty(stillPending.AssignedTableIds);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            (await db.Tables.SingleAsync(value => value.TableId == second.TableId)).IsActive = true;
            await db.SaveChangesAsync();
        }

        var confirmed = await Read<ReservationDto>(await Send(HttpMethod.Post,
            $"/api/reservations/{pending.ReservationId}/confirm", "waiter",
            new { tableIds = new[] { second.TableId, location.TableId } }));
        Assert.Equal(2, confirmed.AssignedTableIds.Count);
    }

    [SqlServerFact]
    public async Task ConfirmUsesHalfOpenIntervalsIncludingTheThirtyMinuteHold()
    {
        var location = await Location();
        var first = await Pending(location);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(first, location.TableId)).StatusCode);
        var overlap = await Pending(location, first.EndAt.AddMinutes(30).AddSeconds(-1));
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(overlap, location.TableId)).StatusCode);
        var adjacent = await Pending(location, first.EndAt.AddMinutes(30));
        Assert.Equal(HttpStatusCode.OK, (await Confirm(adjacent, location.TableId)).StatusCode);
    }

    private async Task<HttpResponseMessage[]> Contend(Guid tableId, Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second)
    {
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = new SqlCommand("SELECT TableId FROM [Table] WITH (UPDLOCK, HOLDLOCK) WHERE TableId = @id", blocker, transaction))
        {
            command.Parameters.AddWithValue("@id", tableId);
            await command.ExecuteScalarAsync();
        }

        var one = first();
        var two = second();
        var observed = await WaitForBlocked(blocker.ServerProcessId, 1);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(one, two);
        Assert.True(observed, "No SQL Server lock wait was observed.");
        return responses;
    }

    private async Task<bool> WaitForBlocked(int blockerProcessId, int requiredCount)
    {
        await using var observer = new SqlConnection(_restaurantConnection);
        await observer.OpenAsync();
        var blocked = false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new SqlCommand("WITH Blocked AS (SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id = @id UNION ALL SELECT request.session_id FROM sys.dm_exec_requests AS request JOIN Blocked AS parent ON request.blocking_session_id = parent.session_id) SELECT COUNT(*) FROM Blocked", observer);
            command.Parameters.AddWithValue("@id", blockerProcessId);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= requiredCount)
            {
                blocked = true;
                break;
            }

            await Task.Delay(25);
        }

        return blocked;
    }

    private async Task<LocationIds> Location(bool assignStaff = true)
    {
        var restaurant = await Read<RestaurantDto>(await Send(HttpMethod.Post, "/api/restaurants", "admin", new
        {
            restaurantName = "SQL Restaurant",
            address = "Test Address",
            hotline = "0901234567",
            timeZone = "UTC",
            openingHours = Enumerable.Range(0, 7).Select(day => new { dayOfWeek = day, openAt = "00:00", closeAt = "23:59" })
        }), HttpStatusCode.Created);
        var area = await Read<AreaDto>(await Send(HttpMethod.Post, "/api/areas", "admin", new
        {
            restaurantId = restaurant.RestaurantId,
            areaCode = "DINING",
            areaName = "Dining Room",
            serviceType = "DineIn",
            spaceType = "Indoor"
        }), HttpStatusCode.Created);
        var table = await Read<TableDto>(await Send(HttpMethod.Post, "/api/tables", "admin", new
        {
            areaId = area.AreaId,
            tableCode = "T1",
            capacity = 4
        }), HttpStatusCode.Created);
        if (assignStaff)
        {
            await Grant(restaurant.RestaurantId, "waiter");
            await Grant(restaurant.RestaurantId, "cashier");
        }

        return new LocationIds(restaurant.RestaurantId, area.AreaId, table.TableId);
    }

    private async Task Grant(Guid restaurantId, string role)
    {
        await Read<RestaurantStaffDto>(await Send(HttpMethod.Post, $"/api/restaurants/{restaurantId}/staff", "admin",
            new { userIdRef = _users[role] }), HttpStatusCode.Created);
    }

    private async Task<ReservationDto> Pending(LocationIds location, DateTimeOffset? start = null, int partySize = 4)
    {
        return await Read<ReservationDto>(await Send(HttpMethod.Post, "/api/reservations", null, new
        {
            restaurantId = location.RestaurantId,
            requestedAreaId = location.AreaId,
            contactName = "SQL Guest",
            phone = "+84901234567",
            partySize,
            startAt = (start ?? _clock.Now.AddHours(2)).ToString("O")
        }), HttpStatusCode.Created);
    }

    private Task<HttpResponseMessage> Confirm(ReservationDto reservation, Guid tableId, string role = "waiter")
    {
        return Send(HttpMethod.Post, $"/api/reservations/{reservation.ReservationId}/confirm", role, new { tableIds = new[] { tableId } });
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? role, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (role is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[role]);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _http.SendAsync(request);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var result = await response.Content.ReadFromJsonAsync<ResponseDTO<T>>();
        Assert.True(result!.IsSuccess);
        return result.Result!;
    }

    private static string Connection(string server, string database)
    {
        return new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        try
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

            if (_identity is not null)
            {
                await _identity.DisposeAsync();
            }
        }
        finally
        {
            await DeleteTestDatabase(_restaurantConnection, "CodexReservationRestaurant_");
            await DeleteTestDatabase(_identityConnection, "CodexReservationIdentity_");
        }
    }

    private static async Task DeleteTestDatabase(string connectionString, string expectedPrefix)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return;
        }

        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!database.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(database[expectedPrefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Refusing to delete a database outside the generated test database names.");
        }

        await using var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.EnsureDeletedAsync();
    }

    private sealed record LocationIds(Guid RestaurantId, Guid AreaId, Guid TableId);

    private sealed class SqlClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.Zero).Ticks;

        public DateTimeOffset Now
        {
            get
            {
                return new DateTimeOffset(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            }
            set
            {
                Interlocked.Exchange(ref _ticks, value.UtcTicks);
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
