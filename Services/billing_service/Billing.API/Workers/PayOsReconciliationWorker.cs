using Billing.BLL.Interfaces;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Microsoft.Extensions.Options;

namespace Billing.API.Workers;

public sealed class PayOsReconciliationWorker(IServiceScopeFactory scopes, IOptions<PayOsSettings> settings,
    ILogger<PayOsReconciliationWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetRequiredService<IPayOsClient>().IsConfigured)
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                    var paidSince = clock.GetUtcNow().UtcDateTime.AddHours(-settings.Value.PaidReconcileHours);
                    var batch = await repository.GetProviderBatchAsync(offset, settings.Value.ReconcileBatchSize, paidSince, stoppingToken);
                    foreach (var id in batch.Ids)
                    {
                        try
                        {
                            using var paymentScope = scopes.CreateScope();
                            await paymentScope.ServiceProvider.GetRequiredService<IPayOsPaymentService>().Synchronize(id, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception error)
                        {
                            logger.LogWarning("PayOS reconciliation deferred for payment {PaymentId}; error type {ErrorType}.", id, error.GetType().Name);
                        }
                    }

                    offset = batch.Total == 0 || offset + batch.Ids.Count >= batch.Total ? 0 : offset + batch.Ids.Count;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning("PayOS reconciliation cycle deferred; error type {ErrorType}.", error.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.Value.ReconcileIntervalSeconds, 1, 3600)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
