using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Settings;

namespace Restaurant.API.Reservations;

public sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSettings> settings,
    TimeProvider clock,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.ExpirationEnabled)
        {
            return;
        }

        var seconds = settings.Value.ExpirationIntervalSeconds;
        if (seconds is < 1 or > 3600)
        {
            logger.LogError("Reservation expiration requires an interval between 1 and 3600 seconds.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Processed {Count} expired or no-show reservations.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Reservation expiration failed and will retry on the next interval.");
            }
        }
    }
}
