using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IQrService
{
    Task<PageResult<QrDto>> GetAll(ListQuery query, CancellationToken ct);

    Task<byte[]> GetImage(Guid id, CancellationToken ct);

    Task<QrCreatedDto> Create(Guid tableId, CancellationToken ct);

    Task<QrCreatedDto> Regenerate(Guid tableId, Guid expectedQrId, CancellationToken ct);

    Task<QrTableDto> Resolve(string token, CancellationToken ct);
}
