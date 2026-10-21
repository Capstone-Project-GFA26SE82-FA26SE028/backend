namespace Ordering.BLL.Interfaces;

public interface IKitchenStationAccess
{
    Task RequireAsync(bool write, CancellationToken ct);
}
