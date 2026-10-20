extern alias gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Contracts.Identity;
using Catalog.API.Extensions;
using Catalog.API.GrpcServices;
using Catalog.BLL.Implements;
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
using GatewayItemController = gateway::ApiGateway.Controllers.CatalogController.ItemController;
using GatewayItemGrpc = gateway::Backend.Contracts.Catalog.ItemGrpc;
using GatewayCatalogClientSettings = gateway::ApiGateway.Settings.CatalogClientSettings;

namespace Catalog.Tests;

internal sealed class ItemTestApplication : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestServer _catalog;
    private readonly TestServer _gateway;

    private ItemTestApplication(SqliteConnection connection, TestServer catalog, TestServer gateway)
    {
        _connection = connection;
        _catalog = catalog;
        _gateway = gateway;
        Client = gateway.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager");
    }

    public HttpClient Client { get; }

    public static DateTimeOffset Now { get; } = new(2030, 1, 15, 8, 0, 0, TimeSpan.Zero);

    public static async Task<ItemTestApplication> Create(string initialAvailability = "Inactive")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GrpcServices:Identity"] = "http://identity.test",
            ["CatalogManagement:IdentityTimeoutSeconds"] = "5",
            ["CatalogManagement:MenuManagerRoleIds:0"] = "test-menu-manager",
            ["CatalogManagement:KitchenRoleIds:0"] = "test-kitchen",
            ["CatalogManagement:NonSellingItemAvailabilities:0"] = "Inactive",
            ["ItemManagement:InitialAvailability"] = initialAvailability,
            ["Pricing:AllChannelsCode"] = "AllChannels"
        }).Build();

        var catalog = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddGrpc();
                services.AddCatalogManagement(configuration);
                services.AddSingleton(new UserGrpc.UserGrpcClient(new ItemIdentityInvoker()));
                services.AddSingleton<TimeProvider>(new TestClock());
                services.AddScoped<CatalogDbContext>(_ => CreateDatabase(connection));
                services.AddScoped<ICategoryRepository, CategoryRepository>();
                services.AddScoped<IItemRepository, ItemRepository>();
                services.AddScoped<IPriceRepository, PriceRepository>();
                services.AddScoped<IOptionGroupRepository, OptionGroupRepository>();
                services.AddScoped<IOptionRepository, OptionRepository>();
                services.AddScoped<IItemOptionGroupRepository, ItemOptionGroupRepository>();
                services.AddScoped<IRecipeRepository, RecipeRepository>();
                services.AddScoped<IRecipeService, RecipeService>();
                services.AddScoped<IUnitOfWork, UnitOfWork<CatalogDbContext>>();
                services.AddScoped<IItemTransaction, ItemTransaction>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<ItemGrpcService>();
                    endpoints.MapGrpcService<OptionGroupGrpcService>();
                    endpoints.MapGrpcService<OptionGrpcService>();
                    endpoints.MapGrpcService<ItemOptionGroupGrpcService>();
                    endpoints.MapGrpcService<RecipeGrpcService>();
                });
            }));

        var gateway = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers().AddApplicationPart(typeof(GatewayItemController).Assembly);
                services.Configure<GatewayCatalogClientSettings>(settings =>
                {
                    settings.TimeoutSeconds = 10;
                });
                services.AddGrpcClient<GatewayItemGrpc.ItemGrpcClient>(options =>
                {
                    options.Address = new Uri("http://catalog.test");
                }).ConfigurePrimaryHttpMessageHandler(catalog.CreateHandler);
                services.AddGrpcClient<gateway::Backend.Contracts.Catalog.OptionGroupGrpc.OptionGroupGrpcClient>(options =>
                {
                    options.Address = new Uri("http://catalog.test");
                }).ConfigurePrimaryHttpMessageHandler(catalog.CreateHandler);
                services.AddGrpcClient<gateway::Backend.Contracts.Catalog.OptionGrpc.OptionGrpcClient>(options =>
                {
                    options.Address = new Uri("http://catalog.test");
                }).ConfigurePrimaryHttpMessageHandler(catalog.CreateHandler);
                services.AddGrpcClient<gateway::Backend.Contracts.Catalog.ItemOptionGroupGrpc.ItemOptionGroupGrpcClient>(options =>
                {
                    options.Address = new Uri("http://catalog.test");
                }).ConfigurePrimaryHttpMessageHandler(catalog.CreateHandler);
                services.AddGrpcClient<gateway::Backend.Contracts.Catalog.RecipeGrpc.RecipeGrpcClient>(options =>
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

        var application = new ItemTestApplication(connection, catalog, gateway);
        await using var db = application.OpenDatabase();
        await db.Database.EnsureCreatedAsync();
        return application;
    }

    public CatalogDbContext OpenDatabase()
    {
        return CreateDatabase(_connection);
    }

    public async Task<Guid> AddCategory(string name = "Main Courses", bool active = true)
    {
        var id = Guid.NewGuid();
        await using var db = OpenDatabase();
        db.Categories.Add(new Category
        {
            CategoryId = id,
            CategoryCode = id.ToString("N"),
            CategoryName = name,
            IsActive = active
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<JsonElement> CreateItem(Guid categoryId, string name = "Vegetarian Soup")
    {
        var response = await Client.PostAsJsonAsync("/api/items", new
        {
            categoryId,
            itemName = name,
            description = "  A seasonal vegetable soup.  ",
            imageUrl = "https://example.test/soup.png"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Item created successfully.", body.GetProperty("message").GetString());
        var item = body.GetProperty("result");
        Assert.Equal($"/api/items/{item.GetProperty("itemId").GetGuid()}", response.Headers.Location?.OriginalString);
        return item;
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
        return new ItemTestDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private sealed class ItemIdentityInvoker : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Assert.EndsWith("/GetMe", method.FullName);
            var token = options.Headers?.FirstOrDefault(header => header.Key == "authorization")?.Value;
            if (token == "Bearer revoked")
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session."));
            }

            var reply = new UserReply
            {
                StatusCode = 200,
                IsSuccess = true,
                Result = new UserInfo
                {
                    UserId = "00000000-0000-0000-0000-000000000001",
                    RoleId = token switch
                    {
                        "Bearer manager" => "test-menu-manager",
                        "Bearer kitchen" => "test-kitchen",
                        _ => "ordinary-user"
                    },
                    IsActive = token != "Bearer disabled"
                }
            };
            return new AsyncUnaryCall<TResponse>(Task.FromResult((TResponse)(object)reply),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
                {
                });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ItemTestDbContext(DbContextOptions<CatalogDbContext> options) : CatalogDbContext(options)
    {
        private static long _versionSequence;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Item>().Property(item => item.ItemName).UseCollation("NOCASE");
            modelBuilder.Entity<Item>().Property(item => item.ItemCode).UseCollation("NOCASE");
            modelBuilder.Entity<Item>().Property(item => item.Version).ValueGeneratedNever();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();
            foreach (var entry in ChangeTracker.Entries<Item>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                entry.Entity.Version = BitConverter.GetBytes(Interlocked.Increment(ref _versionSequence));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
