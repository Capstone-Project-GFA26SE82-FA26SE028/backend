using Billing.Common.DTOs;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.DAL.Interfaces;

public interface IPromotionRepository : IGenericRepository<Promotion>
{
    Task<Promotion?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Promotion>> GetAllAsync(PromotionQueryDTO query, CancellationToken ct);
    Task<List<Promotion>> GetActivePromotionsAsync(DateTime now, CancellationToken ct);
    Task<int> GetUsageCountAsync(Guid promotionId, CancellationToken ct);
    Task AddAsync(Promotion promotion, CancellationToken ct);
    Task UpdateAsync(Promotion promotion, CancellationToken ct);
}
