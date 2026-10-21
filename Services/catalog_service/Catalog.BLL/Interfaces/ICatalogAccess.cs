namespace Catalog.BLL.Interfaces;

public interface ICatalogAccess
{
    Task<Guid> RequireMenuManagerAsync(
        CancellationToken cancellationToken);

    Task<Guid> RequireMenuStaffAsync(CancellationToken cancellationToken);
}
