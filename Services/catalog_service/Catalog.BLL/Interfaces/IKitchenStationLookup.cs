using Shared.Common.DTOs;

namespace Catalog.BLL.Interfaces;

public interface IKitchenStationLookup
{
    Task<ResponseDTO?> ValidateAsync(Guid stationId, CancellationToken ct);
}
