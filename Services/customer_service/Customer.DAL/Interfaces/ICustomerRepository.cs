using Customer.DAL.Models;

namespace Customer.DAL.Interfaces;

public interface ICustomerRepository
{
    Task<Models.Customer?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Models.Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken ct);

    Task<(List<Models.Customer> Items, int Total)> GetPageAsync(int page, int pageSize, string? keyword, CancellationToken ct);

    Task<bool> PhoneExistsAsync(string normalizedPhone, Guid excludedId, CancellationToken ct);

    void Create(Models.Customer customer);

    void SetOriginalVersion(Models.Customer customer, byte[] version);
}

