using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.DAL.Interfaces;

public interface ICashRegisterRepository
{
    Task<CashRegister?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CashRegister?> LockByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<CashRegister>> GetPageAsync(CashRegisterQueryDTO query, IReadOnlyList<Guid>? restaurantIds, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid restaurantId, string code, CancellationToken cancellationToken);
    Task<bool> LockRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken);
    Task<CashRegister> AddAsync(CashRegister register);
}
