using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.DAL.Implements;

public sealed class ItemRepository(
    CatalogDbContext context) : IItemRepository
{
    public Task<bool> HasSellingItemsAsync(
        Guid categoryId,
        string[] nonSellingAvailabilities,
        CancellationToken cancellationToken)
    {
        return context.Items.AnyAsync(
            item => item.CategoryId == categoryId
                && !nonSellingAvailabilities.Contains(item.Availability),
            cancellationToken);
    }
}
