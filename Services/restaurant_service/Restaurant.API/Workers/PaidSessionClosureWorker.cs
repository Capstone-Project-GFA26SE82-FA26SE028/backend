using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.DAL.Interfaces;
using Shared.Common.Billing;

namespace Restaurant.API.Workers;

public sealed class PaidSessionClosureWorker(IServiceScopeFactory scopes, IOptions<PaymentSettlementSettings> settings,
    ILogger<PaidSessionClosureWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        if (!PaymentSettlementSignature.IsConfigured(settings.Value.SharedKey))
        {
            logger.LogWarning("Chưa cấu hình PaymentSettlement:SharedKey; không thể tự động đóng phiên đã thanh toán.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ITableSessionRepository>();
                var batch = await repository.GetSettlementCandidatesAsync(offset, settings.Value.BatchSize, stoppingToken);
                foreach (var id in batch.Ids)
                {
                    try
                    {
                        using var sessionScope = scopes.CreateScope();
                        await sessionScope.ServiceProvider.GetRequiredService<ITableSessionService>().TryClosePaidSession(id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning("Đã hoãn đóng phiên thanh toán {SessionId}; loại lỗi {ErrorType}.", id, error.GetType().Name);
                    }
                }

                offset = batch.Total == 0 || offset + batch.Ids.Count >= batch.Total ? 0 : offset + batch.Ids.Count;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning("Đã hoãn chu kỳ đóng phiên thanh toán; loại lỗi {ErrorType}.", error.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Value.RetryIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
