namespace Catalog.DAL.Interfaces;

public interface IItemRepository
{
    Task<bool> HasSellingItemsAsync(
        Guid categoryId,
        string[] nonSellingAvailabilities,
        CancellationToken cancellationToken);
}
