using Ordering.BLL.Interfaces;

namespace Ordering.Tests;

internal sealed class AllowOrderReportAccess : IOrderReportAccess
{
    public Task RequireManager(Guid restaurantId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
