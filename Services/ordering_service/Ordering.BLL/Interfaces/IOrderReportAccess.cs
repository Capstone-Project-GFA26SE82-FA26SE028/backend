namespace Ordering.BLL.Interfaces;

public interface IOrderReportAccess
{
    Task RequireManager(Guid restaurantId, CancellationToken ct);
}
