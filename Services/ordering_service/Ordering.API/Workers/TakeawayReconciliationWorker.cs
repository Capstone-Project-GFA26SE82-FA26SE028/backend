using Ordering.BLL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Models;
using Shared.Persistence.RestaurantIsolation;

namespace Ordering.API.Workers;

public sealed class TakeawayReconciliationWorker(IServiceScopeFactory scopes,
    ILogger<TakeawayReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var discovery = scopes.CreateScope();
                var db = discovery.ServiceProvider.GetRequiredService<OrderingDbContext>();
                // Internal worker discovers owners only; business reads/writes run in separate scopes.
                var restaurantIds = await db.Orders.IgnoreQueryFilters()
                    .Where(order => order.RestaurantRef.HasValue && order.CustomerUserRef.HasValue
                        && (order.Status == "PendingPayment" || order.CancellationRequested))
                    .Select(order => order.RestaurantRef!.Value).Distinct().ToListAsync(stoppingToken);
                foreach (var restaurantId in restaurantIds)
                {
                    using var scope = scopes.CreateScope();
                    scope.ServiceProvider.GetRequiredService<RestaurantDataScope>().Select(restaurantId);
                    await scope.ServiceProvider.GetRequiredService<IOrderService>().ReconcileTakeaway(stoppingToken);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Takeaway reconciliation will retry.");
            }
        }
    }
}
