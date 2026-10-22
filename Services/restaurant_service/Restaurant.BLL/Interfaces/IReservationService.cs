using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IReservationService
{
    Task<ReservationDto> Reject(Guid id, CancellationToken ct);

    Task<ReservationDto> CheckIn(Guid id, CancellationToken ct);

    Task<ReservationDto> Cancel(Guid id, CancellationToken ct);

    Task<ReservationDto> Update(Guid id, UpdateReservationDto request, CancellationToken ct);

    Task<ReservationDto> Confirm(Guid id, ConfirmReservationDto request, CancellationToken ct);

    Task<ReservationDto> Create(CreateReservationDto request, CancellationToken ct);

    Task<PageResult<ReservationDto>> GetAll(ReservationQuery query, CancellationToken ct);

    Task<ReservationDto> GetById(Guid id, CancellationToken ct);

    Task<ReservationAvailabilityDto> GetAvailability(ReservationAvailabilityQuery query, CancellationToken ct);
}
