using Billing.BLL.Interfaces;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.RestaurantIsolation;
using Microsoft.Extensions.Options;

namespace Billing.API.Workers;

public sealed class PayOsReconciliationWorker(IServiceScopeFactory scopes, IOptionsMonitor<PayOsSettings> settings,
    ILogger<PayOsReconciliationWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offsets = new Dictionary<Guid, int>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var discovery = scopes.CreateScope();
                var db = discovery.ServiceProvider.GetRequiredService<BillingDbContext>();
                var restaurantIds = await db.Bills.IgnoreQueryFilters()
                    .Select(bill => bill.RestaurantId).Distinct().ToListAsync(stoppingToken);
                foreach (var restaurantId in restaurantIds)
                {
                    using var scope = scopes.CreateScope();
                    scope.ServiceProvider.GetRequiredService<RestaurantDataScope>().Select(restaurantId);
                    if (!scope.ServiceProvider.GetRequiredService<IPayOsClient>().IsConfigured)
                    {
                        continue;
                    }

                    var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                    var offset = offsets.GetValueOrDefault(restaurantId);
                    var paidSince = clock.GetUtcNow().UtcDateTime.AddHours(-settings.CurrentValue.PaidReconcileHours);
                    var batch = await repository.GetProviderBatchAsync(offset, settings.CurrentValue.ReconcileBatchSize, paidSince, stoppingToken);
                    foreach (var id in batch.Ids)
                    {
                        try
                        {
                            using var paymentScope = scopes.CreateScope();
                            paymentScope.ServiceProvider.GetRequiredService<RestaurantDataScope>().Select(restaurantId);
                            await paymentScope.ServiceProvider.GetRequiredService<IPayOsPaymentService>().Synchronize(id, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception error)
                        {
                            logger.LogWarning("Payment {PaymentId} reconciliation deferred: {ErrorType}.", id, error.GetType().Name);
                        }
                    }

                    offsets[restaurantId] = batch.Total == 0 || offset + batch.Ids.Count >= batch.Total ? 0 : offset + batch.Ids.Count;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning("Đã hoãn chu kỳ đối soát PayOS; loại lỗi {ErrorType}.", error.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.CurrentValue.ReconcileIntervalSeconds, 1, 3600)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
