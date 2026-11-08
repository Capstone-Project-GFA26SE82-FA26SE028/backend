extern alias gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Contracts.Identity;
using Catalog.API.Extensions;
using Catalog.API.GrpcServices;
using Catalog.DAL.Implements;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.UnitOfWorks;
using Xunit;
using GatewayCategoryController = gateway::ApiGateway.Controllers.CatalogController.CategoryController;
using GatewayCategoryGrpc = gateway::Backend.Contracts.Catalog.CategoryGrpc;
using GatewayCatalogClientSettings = gateway::ApiGateway.Settings.CatalogClientSettings;

namespace Catalog.Tests;

public sealed class CategoryFlowTests
{
    [Fact]
    public async Task CrudFlowsThroughHttpGatewayGrpcAndDatabase()
    {
        using var application = await TestApplication.Create();
        var created = await application.CreateCategory("  Đồ uống  ");
        var id = created.GetProperty("categoryId").GetGuid();
        Assert.Equal("Đồ uống", created.GetProperty("categoryName").GetString());
        Assert.Equal("do-uong", created.GetProperty("categoryCode").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var list = await application.Client.GetFromJsonAsync<JsonElement>("/api/categories");
        Assert.Equal(1, list.GetProperty("result").GetProperty("totalCount").GetInt32());

        var update = await application.Client.PutAsJsonAsync($"/api/categories/{id}", new
        {
            categoryName = "Giải khát"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var detail = await application.Client.GetFromJsonAsync<JsonElement>($"/api/categories/{id}");
        Assert.Equal("Giải khát", detail.GetProperty("result").GetProperty("categoryName").GetString());
        Assert.Equal(created.GetProperty("categoryCode").GetString(),
            detail.GetProperty("result").GetProperty("categoryCode").GetString());

        var status = await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
        {
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.GetAsync($"/api/categories/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("Món chay", "mon-chay")]
    [InlineData("ĐỒ UỐNG", "do-uong")]
    [InlineData("  Main   dishes  ", "main-dishes")]
    [InlineData("---Food & Drinks!!!", "food-drinks")]
    [InlineData("Combo 123", "combo-123")]
    [InlineData("Mo\u0301n chay", "mon-chay")]
    public async Task CreateGeneratesSlugFromName(string name, string expectedCode)
    {
        using var application = await TestApplication.Create();
        var created = await application.CreateCategory(name);
        Assert.Equal(expectedCode, created.GetProperty("categoryCode").GetString());
    }

    [Fact]
    public async Task DifferentNamesWithSameSlugAreRejected()
    {
        using var application = await TestApplication.Create();
        await application.CreateCategory("Món chay");
        var response = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = "Mon chay"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Single(await db.Categories.ToListAsync());
    }

    [Fact]
    public async Task SlugLengthMatchesExistingCodeColumn()
    {
        using var application = await TestApplication.Create();
        var created = await application.CreateCategory(new string('a', 50));
        Assert.Equal(50, created.GetProperty("categoryCode").GetString()!.Length);
        var response = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = new string('b', 51)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("---!!!")]
    [InlineData("🍜")]
    [InlineData("   ")]
    [InlineData("Món\nchính")]
    public async Task InvalidNamesAreRejected(string name)
    {
        using var application = await TestApplication.Create();
        var response = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = name
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NameLengthAndDuplicateRulesAreEnforced()
    {
        using var application = await TestApplication.Create();
        await application.CreateCategory("Drinks");
        var duplicate = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = "  DRINKS  "
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var tooLong = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = new string('a', 256)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var second = await application.CreateCategory("Món chính");
        var id = second.GetProperty("categoryId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PutAsJsonAsync($"/api/categories/{id}", new
            {
                categoryName = "Drinks"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.PutAsJsonAsync($"/api/categories/{id}", new
            {
                categoryName = "Món chính"
            })).StatusCode);
    }

    [Fact]
    public async Task RelatedItemsPreventDeleteAndSellingItemsPreventDisable()
    {
        using var application = await TestApplication.Create();
        var created = await application.CreateCategory("Món chính");
        var id = created.GetProperty("categoryId").GetGuid();
        await application.AddItem(id, "test-temporarily-out");
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
            {
                isActive = false
            })).StatusCode);

        await using (var db = application.OpenDatabase())
        {
            var item = await db.Items.SingleAsync();
            item.Availability = "test-discontinued";
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
            {
                isActive = false
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
            {
                isActive = true
            })).StatusCode);
        await using var verification = application.OpenDatabase();
        Assert.Equal("test-discontinued", (await verification.Items.SingleAsync()).Availability);
    }

    [Fact]
    public async Task MissingStatusAndInvalidIdsAreRejected()
    {
        using var application = await TestApplication.Create();
        var created = await application.CreateCategory("Món chính");
        var id = created.GetProperty("categoryId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.GetAsync("/api/categories/not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await application.Client.GetAsync($"/api/categories/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ListSupportsSearchStatusAndPagination()
    {
        using var application = await TestApplication.Create();
        await application.CreateCategory("Món chính");
        var drink = await application.CreateCategory("Đồ uống");
        var id = drink.GetProperty("categoryId").GetGuid();
        await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
        {
            isActive = false
        });
        var page = await application.Client.GetFromJsonAsync<JsonElement>(
            "/api/categories?page=1&pageSize=1&isActive=false&search=" + Uri.EscapeDataString("Đồ"));
        Assert.Equal(1, page.GetProperty("result").GetProperty("totalCount").GetInt32());
        Assert.Single(page.GetProperty("result").GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.GetAsync("/api/categories?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await application.Client.GetAsync("/api/categories?pageSize=101")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-user", HttpStatusCode.Forbidden)]
    [InlineData("admin", HttpStatusCode.Forbidden)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("identity-down", HttpStatusCode.ServiceUnavailable)]
    [InlineData("identity-timeout", HttpStatusCode.GatewayTimeout)]
    public async Task IdentityFailuresAreMappedWithoutDatabaseWrites(
        string? token,
        HttpStatusCode expected)
    {
        using var application = await TestApplication.Create();
        application.Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
        var response = await application.Client.PostAsJsonAsync("/api/categories", new
        {
            categoryName = "Không được lưu"
        });
        Assert.Equal(expected, response.StatusCode);
        await using var db = application.OpenDatabase();
        Assert.Empty(await db.Categories.ToListAsync());
    }

    [Fact]
    public async Task EmptyRoleConfigurationDeniesAccess()
    {
        using var application = await TestApplication.Create(allowRole: false);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await application.Client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task MissingStatusConfigurationDoesNotAssumeItemsAreDiscontinued()
    {
        using var application = await TestApplication.Create(configureNonSellingStatus: false);
        var created = await application.CreateCategory("Món chính");
        var id = created.GetProperty("categoryId").GetGuid();
        await application.AddItem(id, "test-discontinued");
        Assert.Equal(HttpStatusCode.Conflict,
            (await application.Client.PatchAsJsonAsync($"/api/categories/{id}/status", new
            {
                isActive = false
            })).StatusCode);
    }

    private sealed class TestApplication : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestServer _catalog;
        private readonly TestServer _gateway;

        private TestApplication(SqliteConnection connection, TestServer catalog, TestServer gateway)
        {
            _connection = connection;
            _catalog = catalog;
            _gateway = gateway;
            Client = gateway.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager");
        }

        public HttpClient Client { get; }

        public static async Task<TestApplication> Create(
            bool allowRole = true,
            bool configureNonSellingStatus = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GrpcServices:Identity"] = "http://identity.test",
                ["GrpcServices:Catalog"] = "http://catalog.test",
                ["CatalogManagement:IdentityTimeoutSeconds"] = "5",
                ["CatalogManagement:MenuManagerRoleIds:0"] = allowRole ? "test-menu-manager" : string.Empty,
                ["CatalogManagement:NonSellingItemAvailabilities:0"] = configureNonSellingStatus
                    ? "test-discontinued"
                    : string.Empty,
                ["CatalogClient:TimeoutSeconds"] = "10"
            }).Build();

            var catalog = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddGrpc();
                    services.AddCatalogManagement(configuration);
                    services.AddSingleton(new UserGrpc.UserGrpcClient(new IdentityInvoker()));
                    services.AddScoped<CatalogDbContext>(_ => CreateDatabase(connection));
                    services.AddScoped<ICategoryRepository, CategoryRepository>();
                    services.AddScoped<IItemRepository, ItemRepository>();
                    services.AddScoped<IUnitOfWork, UnitOfWork<CatalogDbContext>>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<CategoryGrpcService>();
                    });
                }));

            var gateway = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(GatewayCategoryController).Assembly);
                    services.AddOptions<GatewayCatalogClientSettings>()
                        .Bind(configuration.GetSection("CatalogClient"))
                        .Validate(settings => settings.TimeoutSeconds > 0,
                            "CatalogClient:TimeoutSeconds phải lớn hơn 0.")
                        .ValidateOnStart();
                    services.AddGrpcClient<GatewayCategoryGrpc.CategoryGrpcClient>(options =>
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

            var application = new TestApplication(connection, catalog, gateway);
            await using var db = application.OpenDatabase();
            await db.Database.EnsureCreatedAsync();
            return application;
        }

        public CatalogDbContext OpenDatabase()
        {
            return CreateDatabase(_connection);
        }

        public async Task<JsonElement> CreateCategory(string name)
        {
            var response = await Client.PostAsJsonAsync("/api/categories", new
            {
                categoryName = name
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var category = body.GetProperty("result");
            Assert.Equal($"/api/categories/{category.GetProperty("categoryId").GetGuid()}",
                response.Headers.Location?.OriginalString);
            return category;
        }

        public async Task AddItem(Guid categoryId, string availability)
        {
            await using var db = OpenDatabase();
            db.Items.Add(new Item
            {
                ItemId = Guid.NewGuid(),
                CategoryId = categoryId,
                ItemCode = Guid.NewGuid().ToString("N"),
                ItemName = "Test item",
                Description = "Test description",
                ImageUrl = "https://example.test/image.png",
                Availability = availability,
                Version = [1]
            });
            await db.SaveChangesAsync();
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
            return new TestCatalogContext(new DbContextOptionsBuilder<CatalogDbContext>()
                .UseSqlite(connection).Options);
        }
    }

    private sealed class TestCatalogContext(DbContextOptions<CatalogDbContext> options) : CatalogDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Category>().Property(category => category.CategoryName).UseCollation("NOCASE");
            modelBuilder.Entity<Item>().Property(item => item.Version).ValueGeneratedNever();
        }
    }

    private sealed class IdentityInvoker : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Assert.EndsWith("/GetMe", method.FullName);
            var token = options.Headers?.FirstOrDefault(header => header.Key == "authorization")?.Value;
            Task<TResponse> response;
            var status = token switch
            {
                "Bearer revoked" => StatusCode.Unauthenticated,
                "Bearer identity-down" => StatusCode.Unavailable,
                "Bearer identity-timeout" => StatusCode.DeadlineExceeded,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK)
            {
                response = Task.FromException<TResponse>(new RpcException(new Status(status, "Test Identity failure")));
            }
            else
            {
                response = Task.FromResult((TResponse)(object)new UserReply
                {
                    StatusCode = 200,
                    IsSuccess = true,
                    Result = new UserInfo
                    {
                        UserId = Guid.NewGuid().ToString(),
                        RoleId = token switch
                        {
                            "Bearer manager" => "test-menu-manager",
                            "Bearer admin" => "admin",
                            _ => "test-ordinary-user"
                        },
                        IsActive = token != "Bearer disabled"
                    }
                });
            }

            return new AsyncUnaryCall<TResponse>(response, Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () =>
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
}
