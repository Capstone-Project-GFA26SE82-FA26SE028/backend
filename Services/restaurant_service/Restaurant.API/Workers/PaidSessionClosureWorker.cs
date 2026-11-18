using Restaurant.BLL.Interfaces;
using Restaurant.DAL.Interfaces;
using Shared.Common.Billing;

namespace Restaurant.API.Workers;

public sealed class PaidSessionClosureWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<PaidSessionClosureWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!PaymentSettlementServiceAccess.IsConfigured(configuration[PaymentSettlementServiceAccess.ConfigurationKey]))
        {
            logger.LogWarning("Chưa cấu hình xác thực nội bộ cho tác vụ đóng phiên đã thanh toán.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var offset = 0;
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var requests = scope.ServiceProvider.GetRequiredService<IServiceRequestRepository>();
                var batch = await requests.GetClosureCandidatesAsync(offset, BatchSize, stoppingToken);
                foreach (var (restaurantId, sessionId) in batch.Where(value => value.RestaurantId != Guid.Empty))
                {
                    try
                    {
                        using var sessionScope = scopes.CreateScope();
                        var service = sessionScope.ServiceProvider.GetRequiredService<ITableSessionService>();
                        await service.CloseAfterPayment(restaurantId, sessionId, stoppingToken);
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Đóng phiên {SessionId} sẽ được thử lại. Loại lỗi {ErrorType}.",
                            sessionId, error.GetType().Name);
                    }
                }

                offset = batch.Count < BatchSize ? 0 : offset + BatchSize;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Chưa thể đối chiếu các phiên chờ đóng. Loại lỗi {ErrorType}.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
