namespace Catalog.BLL.Interfaces;

public interface ICatalogAccess
{
    Task<Guid> RequireMenuManagerAsync(
        CancellationToken cancellationToken);
}
