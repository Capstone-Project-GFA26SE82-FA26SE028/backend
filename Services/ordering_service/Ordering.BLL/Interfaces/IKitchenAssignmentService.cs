using Ordering.Common.DTOs;
using Shared.Common.Pagination;

namespace Ordering.BLL.Interfaces;

public interface IKitchenAssignmentService
{
    Task<PagedResult<KitchenAssignmentDTO>> GetAll(int page, int pageSize, CancellationToken ct);
    Task<KitchenAssignmentDTO> GetById(Guid stationId, CancellationToken ct);
    Task<IReadOnlyList<KitchenEmployeeDTO>> GetEmployees(CancellationToken ct);
    Task<KitchenAssignmentDTO> Create(Guid stationId, Guid userId, CancellationToken ct);
    Task<KitchenAssignmentDTO> Update(Guid stationId, Guid userId, Guid expectedUserId, CancellationToken ct);
    Task Delete(Guid stationId, Guid expectedUserId, CancellationToken ct);
}
