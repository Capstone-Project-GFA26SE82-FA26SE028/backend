using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;

namespace Catalog.DAL.Implements;

public sealed class CategoryRepository(
    CatalogDbContext context) : ICategoryRepository
{
    public async Task<PagedResult<CategoryDTO>> GetPageAsync(
        CategoryQueryDTO query,
        CancellationToken cancellationToken)
    {
        var source = context.Categories.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            source = source.Where(
                category => category.IsActive == query.IsActive.Value);
        }

        var search = query.Search?.Trim();

        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(category =>
                category.CategoryCode.Contains(search)
                || category.CategoryName.Contains(search));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(category => category.CategoryName)
            .ThenBy(category => category.CategoryId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(category => new CategoryDTO
            {
                CategoryId = category.CategoryId,
                CategoryCode = category.CategoryCode,
                CategoryName = category.CategoryName,
                IsActive = category.IsActive
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryDTO>(
            items,
            totalCount,
            query.Page,
            query.PageSize);
    }

    public Task<Category?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return context.Categories.SingleOrDefaultAsync(
            category => category.CategoryId == id,
            cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        string name,
        Guid excludedId,
        CancellationToken cancellationToken)
    {
        return context.Categories.AnyAsync(
            category => category.CategoryId != excludedId
                && category.CategoryName == name,
            cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        string code,
        CancellationToken cancellationToken)
    {
        return context.Categories.AnyAsync(
            category => category.CategoryCode == code,
            cancellationToken);
    }

    public Task<bool> HasItemsAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return context.Items.AnyAsync(
            item => item.CategoryId == categoryId,
            cancellationToken);
    }

    public void Create(Category category)
    {
        context.Categories.Add(category);
    }

    public void Delete(Category category)
    {
        context.Categories.Remove(category);
    }
}
