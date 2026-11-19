using Shared.Common.DTOs;

namespace Identity.BLL.Interfaces;

public interface IStaffTransferService
{
    Task<ResponseDTO> Create(Guid actorId, Guid id, Guid userId, Guid targetRestaurantId, CancellationToken ct);
    Task<ResponseDTO> GetById(Guid actorId, Guid id, CancellationToken ct);
    Task Process(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetPending(CancellationToken ct);
}
