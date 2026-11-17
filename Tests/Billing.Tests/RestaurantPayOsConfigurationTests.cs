using System.Net;
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
    public async Task ExistingChannelCannotBeReplacedButItsKeysCanBeRotated()
    {
        var scope = new RestaurantDataScope();
        using var db = new BillingDbContext(options, scope);
        var service = Service(db, scope, first);
        await service.Update(first, Request("first"), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => service.Update(first, Request("second"), default))).StatusCode);
        var replacement = Request("first");
        replacement.ApiKey = "rotated-api";
        replacement.ChecksumKey = "rotated-checksum";
        await service.Update(first, replacement, default);
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

    private PaymentProviderConfigService Service(BillingDbContext db, RestaurantDataScope scope, Guid assigned, string role = "admin")
    {
        return new PaymentProviderConfigService(new PaymentProviderConfigRepository(db), new UnitOfWork<BillingDbContext>(db),
            new Actor(assigned, role), scope, secrets, Options.Create(new PaymentProviderConfigurationSettings
            {
                WebhookBaseUrl = "https://gateway.example"
            }));
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
