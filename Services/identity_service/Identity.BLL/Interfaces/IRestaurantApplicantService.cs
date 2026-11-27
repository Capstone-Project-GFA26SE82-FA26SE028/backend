namespace Identity.BLL.Interfaces;

public interface IRestaurantApplicantService
{
    Task<RestaurantApplicant> Reserve(Guid registrationId, Guid? currentUserId, string email, string password,
        string fullName, string phone, CancellationToken ct);
    Task<RestaurantApplicant> Activate(Guid registrationId, Guid userId, Guid restaurantId, bool newAccount,
        Guid reviewerId, CancellationToken ct);
}

public sealed record RestaurantApplicant(Guid UserId, string Email);
