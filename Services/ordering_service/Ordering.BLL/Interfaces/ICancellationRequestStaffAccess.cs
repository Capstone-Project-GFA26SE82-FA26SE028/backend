namespace Ordering.BLL.Interfaces;

public interface ICancellationRequestStaffAccess
{
    Task RequireAsync(CancellationToken ct);
}
