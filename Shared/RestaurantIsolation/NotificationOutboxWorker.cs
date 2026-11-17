using System.Text.Json;
using Backend.Contracts.Notifications;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;
using Shared.Persistence.Notifications;
using Shared.Persistence.RestaurantIsolation;

namespace RestaurantIsolation;

public interface INotificationEventEnricher
{
    Task Enrich(NotificationEvent value, CancellationToken ct);
}

public sealed class NotificationOutboxWorker<TContext>(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<NotificationOutboxWorker<TContext>> logger) : BackgroundService where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var key = configuration["Notifications:ServiceKey"];
            if (string.IsNullOrEmpty(key) || key.Length < 32)
            {
                continue;
            }

            try
            {
                using var discovery = scopes.CreateScope();
                var repository = discovery.ServiceProvider.GetRequiredService<NotificationOutboxRepository<TContext>>();
                foreach (var item in await repository.GetPendingAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        scope.ServiceProvider.GetRequiredService<RestaurantDataScope>().Select(item.RestaurantId);
                        var payload = JsonSerializer.Deserialize<NotificationEvent>(item.Payload)
                            ?? throw new InvalidOperationException("InvalidNotificationPayload");
                        var enricher = scope.ServiceProvider.GetService<INotificationEventEnricher>();
                        if (enricher is not null)
                        {
                            await enricher.Enrich(payload, stoppingToken);
                        }

                        var client = scope.ServiceProvider.GetRequiredService<NotificationGrpc.NotificationGrpcClient>();
                        var reply = await client.PublishAsync(new NotificationRequest
                        {
                            Json = JsonSerializer.Serialize(payload)
                        }, new Metadata { { "x-notification-key", key } }, DateTime.UtcNow.AddSeconds(10), stoppingToken);
                        if (reply.StatusCode != 200)
                        {
                            await repository.RetryAsync(item.NotificationOutboxId, item.Attempts, $"NotificationStatus:{reply.StatusCode}", stoppingToken);
                            continue;
                        }

                        await repository.CompleteAsync(item.NotificationOutboxId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        await repository.RetryAsync(item.NotificationOutboxId, item.Attempts, exception.GetType().Name, stoppingToken);
                        logger.LogWarning("Chưa gửi được sự kiện thông báo {EventId}; sẽ thử lại.", item.NotificationOutboxId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Tạm hoãn xử lý thông báo: {ErrorType}.", exception.GetType().Name);
            }
        }
    }
}
