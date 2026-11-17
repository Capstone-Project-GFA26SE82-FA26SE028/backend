using System.Text.Json;
using Backend.Contracts.Notifications;
using Billing.DAL.Models;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantIsolation;
using Shared.Common.Notifications;
using Shared.Persistence.Notifications;
using Shared.Persistence.RestaurantIsolation;

namespace Billing.Tests;

public sealed class NotificationOutboxWorkerTests
{
    [Fact]
    public async Task WorkerRetriesRemoteFailureAndMarksOnlyAcknowledgedEventProcessed()
    {
        await using var anchor = new SqliteConnection($"Data Source=notify-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(anchor.ConnectionString).Options;
        var id = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await using (var db = new BillingDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Set<NotificationOutbox>().Add(new NotificationOutbox
            {
                NotificationOutboxId = id,
                RestaurantId = restaurantId,
                EventKey = "retry-test",
                CreatedAt = DateTime.UtcNow,
                NextAttemptAt = DateTime.UtcNow,
                Payload = JsonSerializer.Serialize(new NotificationEvent
                {
                    RestaurantId = restaurantId,
                    Type = "CheckoutRequested",
                    EventKey = "retry-test",
                    EntityId = Guid.NewGuid(),
                    OccurredAt = DateTime.UtcNow
                })
            });
            await db.SaveChangesAsync();
        }
        var fake = new RetryClient();
        var services = new ServiceCollection();
        services.AddScoped(_ => new BillingDbContext(options));
        services.AddScoped<RestaurantDataScope>();
        services.AddScoped<NotificationOutboxRepository<BillingDbContext>>();
        services.AddSingleton<NotificationGrpc.NotificationGrpcClient>(fake);
        await using var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:ServiceKey"] = new string('k', 40)
        }).Build();
        using var worker = new NotificationOutboxWorker<BillingDbContext>(provider.GetRequiredService<IServiceScopeFactory>(),
            config, NullLogger<NotificationOutboxWorker<BillingDbContext>>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await worker.StartAsync(timeout.Token);
        try
        {
            var processed = false;
            while (!processed)
            {
                await Task.Delay(200, timeout.Token);
                await using var db = new BillingDbContext(options);
                var value = await db.Set<NotificationOutbox>().AsNoTracking().SingleAsync(timeout.Token);
                processed = value.ProcessedAt.HasValue;
                if (processed)
                {
                    Assert.Equal(1, value.Attempts);
                    Assert.Null(value.LastError);
                }
            }
            Assert.Equal(2, fake.Calls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RetryClient : NotificationGrpc.NotificationGrpcClient
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<NotificationReply> PublishAsync(NotificationRequest request, CallOptions options)
        {
            Assert.Equal(new string('k', 40), options.Headers!.GetValue("x-notification-key"));
            Calls++;
            var reply = new NotificationReply
            {
                StatusCode = Calls == 1 ? 503 : 200
            };
            return new AsyncUnaryCall<NotificationReply>(Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }
}
