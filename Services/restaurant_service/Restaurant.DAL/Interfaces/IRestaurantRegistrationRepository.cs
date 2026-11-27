using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface IRestaurantRegistrationRepository
{
    Task<RestaurantRegistration?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RestaurantRegistration?> GetForApplicantAsync(Guid userId, CancellationToken ct);
    Task<(List<RestaurantRegistration> Items, int TotalCount)> GetAllAsync(string? status, int page, int pageSize, CancellationToken ct);
    void Create(RestaurantRegistration registration);
}
