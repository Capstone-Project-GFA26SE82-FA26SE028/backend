using Identity.BLL.Interfaces;

namespace Identity.API.Workers;

public sealed class StaffProvisioningWorker(IServiceScopeFactory scopes, ILogger<StaffProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IUserService>().ProvisionPending(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Chưa hoàn tất phân công nhân viên; hệ thống sẽ thử lại.");
            }
        }
    }
}
