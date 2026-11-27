namespace Restaurant.BLL.Interfaces;

public interface IRestaurantApplicantClient
{
    Task<RestaurantApplicant> Reserve(Guid registrationId, RestaurantApplicantCredentials credentials, CancellationToken ct);
    Task Activate(Guid registrationId, Guid userId, Guid restaurantId, bool newAccount, CancellationToken ct);
}

public sealed record RestaurantApplicantCredentials(string Email, string Password, string FullName, string PhoneNumber);
public sealed record RestaurantApplicant(Guid UserId, string Email, bool NewAccount);
