namespace Restaurant.BLL.Interfaces;

public interface IManagementAccess
{
    Task<Guid> RequireAdmin(CancellationToken ct);
}
