namespace Identity.BLL.Interfaces;

public interface IRestaurantNames
{
    Task<IReadOnlyDictionary<Guid, string>> GetAsync(Guid[] ids, CancellationToken ct);
}
