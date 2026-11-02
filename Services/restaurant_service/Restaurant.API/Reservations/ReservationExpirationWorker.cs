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
            logger.LogError("Chu kỳ xử lý đặt bàn hết hạn phải từ 1 đến 3600 giây.");
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
                    logger.LogInformation("Đã xử lý {Count} yêu cầu đặt bàn hết hạn hoặc khách không đến.", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Xử lý đặt bàn hết hạn thất bại và sẽ thử lại ở chu kỳ tiếp theo.");
            }
        }
    }
}
