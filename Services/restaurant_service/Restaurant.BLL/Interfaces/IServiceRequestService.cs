using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IServiceRequestService
{
    Task<ServiceRequestDto> Create(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<ServiceRequestDto> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct);
    Task<PageResult<ServiceRequestDto>> GetAll(Guid restaurantId, string? status, int page, int pageSize, CancellationToken ct);
    Task<ServiceRequestDto> Acknowledge(Guid sessionId, Guid id, CancellationToken ct);
    Task<ServiceRequestDto> Review(Guid sessionId, Guid id, CancellationToken ct);
    Task<ServiceRequestDto> Cancel(Guid sessionId, Guid id, CancellationToken ct);
}
