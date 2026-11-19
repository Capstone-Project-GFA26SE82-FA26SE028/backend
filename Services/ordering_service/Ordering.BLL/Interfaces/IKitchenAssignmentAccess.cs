using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IKitchenAssignmentAccess
{
    Task RequireManager(CancellationToken ct);
    Task<IReadOnlyList<KitchenEmployeeDTO>> GetEmployees(CancellationToken ct);
    Task RequireEmployee(Guid userId, CancellationToken ct);
}
