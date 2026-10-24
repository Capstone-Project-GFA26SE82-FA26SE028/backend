using Customer.DAL.Interfaces;
using Customer.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Customer.DAL.Implements;

public sealed class CustomerRepository(CustomerDbContext db) : ICustomerRepository
{
    public Task<Models.Customer?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Customers
            .Include(c => c.LoyaltyTier)
            .FirstOrDefaultAsync(c => c.CustomerId == id, ct);
    }

    public Task<Models.Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken ct)
    {
        return db.Customers
            .Include(c => c.LoyaltyTier)
            .FirstOrDefaultAsync(c => c.NormalizedPhone == normalizedPhone, ct);
    }

    public async Task<(List<Models.Customer> Items, int Total)> GetPageAsync(
        int page,
        int pageSize,
        string? keyword,
        CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking().Include(c => c.LoyaltyTier).AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(c => c.NormalizedPhone.Contains(trimmed) || c.CustomerName.Contains(trimmed));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(c => c.CustomerName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<bool> PhoneExistsAsync(string normalizedPhone, Guid excludedId, CancellationToken ct)
    {
        return db.Customers.AnyAsync(
            c => c.CustomerId != excludedId && c.NormalizedPhone == normalizedPhone,
            ct);
    }

    public void Create(Models.Customer customer)
    {
        db.Customers.Add(customer);
    }

    public void SetOriginalVersion(Models.Customer customer, byte[] version)
    {
        db.Entry(customer).Property(c => c.Version).OriginalValue = version;
    }
}

