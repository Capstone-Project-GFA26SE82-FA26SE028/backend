using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IPromotionService
{
    Task<PromotionDTO> Create(CreatePromotionDTO dto, CancellationToken ct);
    Task<PromotionDTO> Update(Guid id, UpdatePromotionDTO dto, CancellationToken ct);
    Task<PagedResult<PromotionDTO>> GetAll(PromotionQueryDTO query, CancellationToken ct);
    Task<PromotionDTO> GetById(Guid id, CancellationToken ct);
    Task<PromotionDTO> SetStatus(Guid id, bool isActive, CancellationToken ct);
    Task<List<ApplicablePromotionDTO>> GetAvailableForBill(Guid billId, CancellationToken ct);
}
