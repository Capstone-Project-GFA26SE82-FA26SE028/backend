namespace Restaurant.BLL.Interfaces;

public interface ITableSessionOrderAccess
{
    Task<Guid> RequireStaffAsync(CancellationToken cancellationToken);
}
