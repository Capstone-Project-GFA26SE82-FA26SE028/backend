using Identity.BLL.Interfaces;

namespace Identity.API.Workers;

public sealed class StaffTransferWorker(IServiceScopeFactory scopes, ILogger<StaffTransferWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var discovery = scopes.CreateScope();
                var ids = await discovery.ServiceProvider.GetRequiredService<IStaffTransferService>().GetPending(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<IStaffTransferService>().Process(id, stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Chưa đồng bộ xong yêu cầu chuyển nhân viên {TransferId}; sẽ thử lại.", id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Tạm hoãn tác vụ chuyển nhân viên; sẽ thử lại.");
            }
        }
    }
}
