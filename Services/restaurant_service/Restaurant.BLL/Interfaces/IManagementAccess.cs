namespace Restaurant.BLL.Interfaces;

public interface IManagementAccess
{
    Task<Guid> RequireAdmin(CancellationToken ct);
    Task<Guid> RevalidateAdmin(CancellationToken ct);
}
