using Backend.Contracts.Restaurant;
using Identity.BLL.Interfaces;

namespace Identity.API.Clients;

public sealed class RestaurantNamesClient(RestaurantGrpc.RestaurantGrpcClient client) : IRestaurantNames
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var request = new RestaurantNamesRequest();
        request.RestaurantIds.AddRange(ids.Select(id => id.ToString()));
        var reply = await client.GetNamesAsync(request, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        return reply.Names.ToDictionary(item => Guid.Parse(item.Key), item => item.Value);
    }
}
