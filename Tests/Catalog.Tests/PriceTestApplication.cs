extern alias gateway;

using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.API.Extensions;
using Catalog.API.GrpcServices;
using Catalog.BLL.Interfaces;
using Catalog.DAL.Implements;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.UnitOfWorks;
using Xunit;
using GatewayPriceController = gateway::ApiGateway.Controllers.CatalogController.PriceController;
using GatewayPriceGrpc = gateway::Backend.Contracts.Catalog.PriceGrpc;
using GatewayCatalogClientSettings = gateway::ApiGateway.Settings.CatalogClientSettings;

namespace Catalog.Tests;

internal sealed class PriceTestApplication : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestServer _catalog;
    private readonly TestServer _gateway;

    private PriceTestApplication(SqliteConnection connection, TestServer catalog, TestServer gateway, TestClock clock)
    {
        _connection = connection;
        _catalog = catalog;
        _gateway = gateway;
        Clock = clock;
        Client = gateway.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager");
    }

    public HttpClient Client { get; }
    public TestClock Clock { get; }

    public static async Task<PriceTestApplication> Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new TestClock();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcServices:Identity"] = "http://identity.test",
            ["CatalogManagement:IdentityTimeoutSeconds"] = "5",
            ["CatalogManagement:MenuManagerRoleIds:0"] = "test-menu-manager",
            ["CatalogManagement:NonSellingItemAvailabilities:0"] = "Inactive",
            ["ItemManagement:InitialAvailability"] = "Inactive",
            ["Pricing:AllChannelsCode"] = "AllChannels",
            ["Pricing:Channels:0"] = "DineIn",
            ["Pricing:BusinessTimeZone"] = "Asia/Ho_Chi_Minh"
        }).Build();
        var catalog = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddGrpc();
                services.AddSingleton<TimeProvider>(clock);
                services.AddCatalogManagement(configuration);
                services.AddScoped<ICatalogAccess, TestMenuAccess>();
                services.AddScoped<CatalogDbContext>(_ => CreateDatabase(connection));
                services.AddScoped<IItemRepository, ItemRepository>();
                services.AddScoped<IPriceRepository, PriceRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork<CatalogDbContext>>();
                services.AddSingleton<TestLocks>();
                services.AddScoped<IPriceTransaction, TestPriceTransaction>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<PriceGrpcService>();
                });
            }));
        var gateway = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers().AddApplicationPart(typeof(GatewayPriceController).Assembly);
                services.Configure<GatewayCatalogClientSettings>(settings =>
                {
                    settings.TimeoutSeconds = 10;
                });
                services.AddGrpcClient<GatewayPriceGrpc.PriceGrpcClient>(options =>
                {
                    options.Address = new Uri("http://catalog.test");
                }).ConfigurePrimaryHttpMessageHandler(catalog.CreateHandler);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            }));
        var application = new PriceTestApplication(connection, catalog, gateway, clock);
        await using var db = application.OpenDatabase();
        await db.Database.EnsureCreatedAsync();
        return application;
    }

    public CatalogDbContext OpenDatabase()
    {
        return CreateDatabase(_connection);
    }

    public async Task<Guid> AddItem(string availability = "Inactive")
    {
        await using var db = OpenDatabase();
        var category = new Category
        {
            CategoryId = Guid.NewGuid(),
            CategoryCode = Guid.NewGuid().ToString("N"),
            CategoryName = "Main Courses",
            IsActive = true
        };
        var item = new Item
        {
            ItemId = Guid.NewGuid(),
            CategoryId = category.CategoryId,
            Category = category,
            ItemCode = Guid.NewGuid().ToString("N"),
            ItemName = "Vegetable Soup",
            Availability = availability,
            Version = new byte[8]
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.ItemId;
    }

    public async Task<JsonElement> CreatePrice(
        Guid itemId, decimal amount = 100m, string channel = "AllChannels",
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        string? dailyStart = null, string? dailyEnd = null)
    {
        var response = await Client.PostAsJsonAsync("/api/prices", new
        {
            itemId,
            channel,
            amount,
            effectiveFrom = (from ?? Clock.GetUtcNow().AddDays(-1)).ToString("O"),
            effectiveTo = to?.ToString("O"),
            dailyStart,
            dailyEnd
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        var body = JsonSerializer.Deserialize<JsonElement>(text);
        var price = body.GetProperty("result");
        Assert.Equal($"/api/prices/{price.GetProperty("priceId").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Null(response.Headers.ETag);
        Assert.False(price.TryGetProperty("concurrencyToken", out _));
        return price;
    }

    public async Task<HttpResponseMessage> Mutate(
        HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    public async Task<JsonElement> Applied(Guid itemId, DateTimeOffset? at = null)
    {
        var route = $"/api/items/{itemId}/price?channel=DineIn";
        if (at.HasValue)
        {
            route += "&at=" + Uri.EscapeDataString(at.Value.ToString("O"));
        }

        var response = await Client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonSerializer.Deserialize<JsonElement>(text).GetProperty("result");
    }

    public void Dispose()
    {
        Client.Dispose();
        _gateway.Dispose();
        _catalog.Dispose();
        _connection.Dispose();
    }

    private static CatalogDbContext CreateDatabase(SqliteConnection connection)
    {
        return new TestCatalogContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);
    }

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 15, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    private sealed class TestCatalogContext(DbContextOptions<CatalogDbContext> options) : CatalogDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Item>().Property(item => item.Version).ValueGeneratedNever();
        }
    }

    private sealed class TestLocks
    {
        public ConcurrentDictionary<Guid, SemaphoreSlim> Items { get; } = new();
    }

    private sealed class TestPriceTransaction(IUnitOfWork unitOfWork, TestLocks locks) : IPriceTransaction
    {
        public async Task<T> ExecuteAsync<T>(Guid itemId, Func<Task<T>> action, CancellationToken cancellationToken)
        {
            var gate = locks.Items.GetOrAdd(itemId, _ => new SemaphoreSlim(1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await unitOfWork.ExecuteInTransactionAsync(action, IsolationLevel.Serializable, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private sealed class TestMenuAccess(IHttpContextAccessor accessor) : ICatalogAccess
    {
        public Task<Guid> RequireMenuManagerAsync(CancellationToken cancellationToken)
        {
            var token = accessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(token) || token == "Bearer revoked")
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session."));
            }

            if (token != "Bearer manager")
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Access denied."));
            }

            return Task.FromResult(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        }
    }
}
