extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Contracts.Billing;
using Billing.API.Clients;
using Billing.API.GrpcServices;
using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gateway::Backend.Contracts.Billing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Persistence.RestaurantIsolation;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class RestaurantPayOsConfigurationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly PaymentProviderSecrets secrets = new(new EphemeralDataProtectionProvider());
    private readonly Guid first = Guid.NewGuid();
    private readonly Guid second = Guid.NewGuid();
    private readonly DbContextOptions<BillingDbContext> options;

    public RestaurantPayOsConfigurationTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(connection).Options;
        using var db = new BillingDbContext(options);
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task RestaurantAdminStoresEncryptedKeysAndResponseDoesNotExposeThem()
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var service = Service(db, scope, first);
        var saved = await service.Update(first, Request("first"), default);
        var stored = await db.PaymentProviderConfigs.SingleAsync();
        Assert.DoesNotContain("api-first", stored.SecretReference);
        Assert.DoesNotContain("checksum-first", stored.SecretReference);
        Assert.Equal("api-first", secrets.Unprotect(first, stored.SecretReference).ApiKey);
        Assert.Throws<BillingException>(() => secrets.Unprotect(second, stored.SecretReference));
        var response = JsonSerializer.Serialize(await service.GetById(first, default));
        Assert.DoesNotContain("ApiKey", response);
        Assert.DoesNotContain("ChecksumKey", response);
        Assert.Equal($"https://gateway.example/api/restaurants/{first}/payments/payos/webhook", saved.WebhookUrl);
        Assert.False(stored.IsVerified);
    }

    [Theory]
    [InlineData("admin", false)]
    [InlineData("manager", true)]
    [InlineData("platform_admin", true)]
    [InlineData("customer", true)]
    public async Task OtherRestaurantAndOtherRolesCannotReadOrChangeKeys(string role, bool assigned)
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var service = Service(db, scope, assigned ? first : second, role);
        var write = await Assert.ThrowsAsync<BillingException>(() => service.Update(first, Request("first"), default));
        var read = await Assert.ThrowsAsync<BillingException>(() => service.GetById(first, default));
        Assert.Equal(403, write.StatusCode);
        Assert.Equal(403, read.StatusCode);
        Assert.Empty(await db.PaymentProviderConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ExistingConfigurationCanUpdateChannelAndRotateKeys()
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var service = Service(db, scope, first);
        await service.Update(first, Request("first"), default);

        var updatedChannel = await service.Update(first, Request("second"), default);
        Assert.Equal("client-second", updatedChannel.ClientId);
        Assert.Equal("api-second", secrets.Unprotect(first, (await db.PaymentProviderConfigs.SingleAsync()).SecretReference).ApiKey);

        var replacement = Request("second");
        replacement.ApiKey = "rotated-api";
        replacement.ChecksumKey = "rotated-checksum";
        await service.Update(first, replacement, default);
        Assert.Equal("rotated-api", secrets.Unprotect(first, (await db.PaymentProviderConfigs.SingleAsync()).SecretReference).ApiKey);

        var keepKeys = Request("second");
        keepKeys.ApiKey = string.Empty;
        keepKeys.ChecksumKey = string.Empty;
        keepKeys.Enabled = false;
        var kept = await service.Update(first, keepKeys, default);
        Assert.False(kept.Enabled);
        Assert.Equal("rotated-api", secrets.Unprotect(first, (await db.PaymentProviderConfigs.SingleAsync()).SecretReference).ApiKey);
    }

    [Fact]
    public async Task EachRestaurantUsesOwnHttpCredentialsAndResponseSignature()
    {
        await Seed(first, "first");
        await Seed(second, "second");
        foreach (var entry in new[] { (first, "first"), (second, "second") })
        {
            var scope = new RestaurantDataScope();
            scope.Select(entry.Item1);
            using var db = new BillingDbContext(options, scope);
            var selected = Runtime(db, scope);
            var provider = new PayOsClient(new HttpClient(new Handler(request =>
            {
                Assert.Equal("client-" + entry.Item2, request.Headers.GetValues("x-client-id").Single());
                Assert.Equal("api-" + entry.Item2, request.Headers.GetValues("x-api-key").Single());
                var data = JsonSerializer.SerializeToElement(new
                {
                    orderCode = 123,
                    amount = 100,
                    amountPaid = 0,
                    amountRemaining = 100,
                    status = "PENDING",
                    id = "link",
                    transactions = Array.Empty<object>()
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        code = "00",
                        data,
                        signature = PayOsSignature.SignData(data, "checksum-" + entry.Item2)
                    }), Encoding.UTF8, "application/json")
                };
            })), selected, new Host());
            Assert.Equal("PENDING", (await provider.GetAsync(123, default)).Status);
        }
    }

    [Fact]
    public void MissingRestaurantConfigurationNeverFallsBackToSharedCredentials()
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var runtime = Runtime(db, scope);
        Assert.Empty(runtime.Value.ApiKey);
        scope.Select(first);
        Assert.Empty(runtime.Value.ApiKey);
        Assert.False(runtime.Value.Enabled);
    }

    [Fact]
    public async Task WebhookUsesRouteOwnerKeyAndRejectsAnOrderFromAnotherRestaurant()
    {
        await Seed(first, "first");
        await Seed(second, "second");
        var online = new OnlineSpy();
        var webhook = new WebhookSpy();
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var provider = new PayOsClient(new HttpClient(), Runtime(db, scope), new Host());
        var grpc = new PaymentWebhookGrpcService(webhook, online, db, scope, provider);
        var request = new PaymentWebhookRequest
        {
            RestaurantId = first.ToString(),
            Payload = Payload("checksum-second")
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<BillingException>(() => grpc.Process(request, new Context()))).StatusCode);
        request.Payload = Payload("checksum-first");
        Assert.True((await grpc.Process(request, new Context())).Received);
        using (var seed = new BillingDbContext(options))
        {
            seed.OnlinePayments.Add(new OnlinePayment
            {
                OrderId = Guid.NewGuid(),
                RestaurantId = second,
                CustomerUserId = Guid.NewGuid(),
                ProviderOrderCode = 123,
                Amount = 100,
                Status = "Pending",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            await seed.SaveChangesAsync();
        }

        Assert.Equal(StatusCode.InvalidArgument, (await Assert.ThrowsAsync<RpcException>(() => grpc.Process(request, new Context()))).StatusCode);
        Assert.Equal(0, online.Calls);
        Assert.Equal(0, webhook.Calls);
    }

    private async Task Seed(Guid restaurantId, string suffix)
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        await Service(db, scope, restaurantId).Update(restaurantId, Request(suffix), default);
    }

    [Fact]
    public async Task TestQrUsesSavedOrNewCredentialsWithoutSavingChanges()
    {
        await Seed(first, "first");
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var provider = new QrClient();
        var service = Service(db, scope, first, testClient: provider);

        var result = await service.TestQr(first, new(), default);
        Assert.Equal("test-qr", result.QrCode);
        Assert.Equal(("client-first", "api-first", "checksum-first"), Assert.Single(provider.Calls));

        await service.TestQr(first, new TestPayOsConfigurationDTO
        {
            ClientId = "client-new",
            ApiKey = "api-new",
            ChecksumKey = "checksum-new"
        }, default);
        Assert.Equal(("client-new", "api-new", "checksum-new"), provider.Calls[1]);
        var saved = await db.PaymentProviderConfigs.SingleAsync();
        Assert.Equal("client-first", saved.ClientId);
        Assert.Equal("api-first", secrets.Unprotect(first, saved.SecretReference).ApiKey);
    }

    [Theory]
    [InlineData("admin", false)]
    [InlineData("manager", true)]
    [InlineData("platform_admin", true)]
    public async Task TestQrDeniesUnauthorizedRequestsInBothModes(string role, bool assigned)
    {
        using var db = new BillingDbContext(options);
        var provider = new QrClient();
        var service = Service(db, new RestaurantDataScope(), assigned ? first : second, role, provider);
        var saved = await Assert.ThrowsAsync<BillingException>(() => service.TestQr(first,
            new TestPayOsConfigurationDTO { UseSavedConfiguration = true }, default));
        var supplied = await Assert.ThrowsAsync<BillingException>(() => service.TestQr(first,
            new TestPayOsConfigurationDTO { ClientId = "client", ApiKey = "api", ChecksumKey = "checksum" }, default));

        Assert.Equal(403, saved.StatusCode);
        Assert.Equal(403, supplied.StatusCode);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task TestQrRejectsMissingConfigurationAndIncompleteOrConflictingCredentials()
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var provider = new QrClient();
        var service = Service(db, scope, first, testClient: provider);

        var missing = await Assert.ThrowsAsync<BillingException>(() => service.TestQr(first, new(), default));
        Assert.Equal(409, missing.StatusCode);
        Assert.Contains("chưa cấu hình PayOS", missing.Message);
        var partial = await Assert.ThrowsAsync<BillingException>(() => service.TestQr(first,
            new TestPayOsConfigurationDTO { ClientId = "client" }, default));
        var mixed = await Assert.ThrowsAsync<BillingException>(() => service.TestQr(first,
            new TestPayOsConfigurationDTO { UseSavedConfiguration = true, ApiKey = "api" }, default));
        Assert.Equal(400, partial.StatusCode);
        Assert.Equal(400, mixed.StatusCode);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task GatewayAcceptsPostWithoutBodyAndOnlyReturnsPublicQrFields()
    {
        await Seed(first, "first");
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var provider = new QrClient();
        var billingBuilder = WebApplication.CreateBuilder();
        billingBuilder.WebHost.UseTestServer();
        billingBuilder.Services.AddGrpc(grpc => grpc.Interceptors.Add<BillingInterceptor>());
        billingBuilder.Services.AddSingleton<IPaymentProviderConfigService>(Service(db, scope, first, testClient: provider));
        await using var billing = billingBuilder.Build();
        billing.MapGrpcService<PaymentProviderConfigGrpcService>();
        await billing.StartAsync();
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = billing.GetTestServer().CreateHandler()
        });

        var gatewayBuilder = WebApplication.CreateBuilder();
        gatewayBuilder.WebHost.UseTestServer();
        gatewayBuilder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        gatewayBuilder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(settings =>
        {
            settings.TimeoutSeconds = 15;
        });
        gatewayBuilder.Services.AddSingleton(new Wire.PaymentProviderConfigGrpc.PaymentProviderConfigGrpcClient(channel));
        await using var gateway = gatewayBuilder.Build();
        gateway.MapControllers();
        await gateway.StartAsync();
        using var http = gateway.GetTestClient();
        var route = $"/api/restaurants/{first}/payment-settings/payos/test-qr";

        using var anonymous = await http.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(provider.Calls);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin");
        using var saved = await http.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("no-store", saved.Headers.CacheControl?.ToString());
        var json = await saved.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal("test-qr", document.RootElement.GetProperty("result").GetProperty("qrCode").GetString());
        Assert.DoesNotContain("api-first", json);
        Assert.DoesNotContain("checksum-first", json);

        using var supplied = await http.PostAsJsonAsync(route, new
        {
            ClientId = "client-new",
            ApiKey = "api-new",
            ChecksumKey = "checksum-new"
        });
        Assert.Equal(HttpStatusCode.OK, supplied.StatusCode);
        Assert.Equal(("client-new", "api-new", "checksum-new"), provider.Calls[1]);
        var suppliedJson = await supplied.Content.ReadAsStringAsync();
        Assert.DoesNotContain("api-new", suppliedJson);
        Assert.DoesNotContain("checksum-new", suppliedJson);

        using var foreign = await http.PostAsJsonAsync($"/api/restaurants/{second}/payment-settings/payos/test-qr",
            new { UseSavedConfiguration = true });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(2, provider.Calls.Count);
    }

    private sealed class QrClient : IPayOsTestClient
    {
        public List<(string Client, string ApiKey, string Checksum)> Calls { get; } = [];

        public Task<PayOsProviderResult> CreateTestAsync(string clientId, PayOsCredentials credentials,
            string returnUrl, string cancelUrl, CancellationToken ct)
        {
            Calls.Add((clientId, credentials.ApiKey, credentials.ChecksumKey));
            return Task.FromResult(new PayOsProviderResult(101, 2000, "PENDING", "test-link",
                null, null, null, "https://pay.example/test", "test-qr"));
        }
    }

    private PaymentProviderConfigService Service(BillingDbContext db, RestaurantDataScope scope, Guid assigned, string role = "admin", IPayOsTestClient? testClient = null)
    {
        return new PaymentProviderConfigService(new PaymentProviderConfigRepository(db), new UnitOfWork<BillingDbContext>(db),
            new Actor(assigned, role), scope, secrets, Options.Create(new PaymentProviderConfigurationSettings
            {
                WebhookBaseUrl = "https://gateway.example"
            }), testClient ?? new QrClient());
    }

    private RestaurantPayOsOptions Runtime(BillingDbContext db, RestaurantDataScope scope)
    {
        return new RestaurantPayOsOptions(scope, new PaymentProviderConfigRepository(db), secrets, new Defaults());
    }

    private static SavePayOsConfigurationDTO Request(string suffix)
    {
        return new SavePayOsConfigurationDTO
        {
            ClientId = "client-" + suffix,
            ApiKey = "api-" + suffix,
            ChecksumKey = "checksum-" + suffix,
            ReturnUrl = "https://ui.example/return",
            CancelUrl = "https://ui.example/cancel",
            Enabled = true
        };
    }

    private static string Payload(string key)
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            orderCode = 123,
            amount = 100,
            reference = "reference",
            paymentLinkId = "link",
            currency = "VND",
            code = "00"
        });
        return JsonSerializer.Serialize(new
        {
            code = "00",
            success = true,
            data,
            signature = PayOsSignature.SignData(data, key)
        });
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private sealed class Actor(Guid assigned, string role) : IBillingAccess
    {
        public Task<BillingAccessScope> GetCurrentScopeAsync(Guid? restaurantId, CancellationToken ct)
        {
            return Task.FromResult(new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = role,
                IsAdministrator = role == "platform_admin",
                RestaurantIds = [assigned]
            });
        }
    }

    private sealed class Defaults : IOptionsMonitor<PayOsSettings>
    {
        public PayOsSettings CurrentValue { get; } = new()
        {
            ApiKey = "shared-key-must-not-be-used",
            ClientId = "shared-client",
            ChecksumKey = "shared-checksum",
            Enabled = true
        };

        public PayOsSettings Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<PayOsSettings, string?> listener)
        {
            return null;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class OnlineSpy : IOnlinePaymentService
    {
        public int Calls { get; private set; }

        public Task<OnlinePaymentDTO> Execute(OnlinePaymentCommand command, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ProcessWebhook(string payload, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }

    private sealed class WebhookSpy : IPaymentWebhookService
    {
        public int Calls { get; private set; }

        public Task Process(string payload, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Context : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "test";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotSupportedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }
}
