using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IKitchenStationService
{
    Task<KitchenStationDTO> Create(KitchenStationWriteDTO request, CancellationToken ct);
    Task<KitchenStationPageDTO> GetAll(int page, int pageSize, string? search, bool? isActive, CancellationToken ct);
    Task<KitchenStationDTO> GetById(Guid id, CancellationToken ct);
    Task<KitchenStationDTO> Update(Guid id, KitchenStationWriteDTO request, CancellationToken ct);
    Task<KitchenStationDTO> SetStatus(Guid id, bool isActive, CancellationToken ct);
}
