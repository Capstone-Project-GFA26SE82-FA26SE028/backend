namespace Restaurant.BLL.Interfaces;

public interface IReservationExpirationService
{
    Task<int> Expire(CancellationToken ct);
}
