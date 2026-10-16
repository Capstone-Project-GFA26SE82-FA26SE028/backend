using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IRestaurantService
{
    Task<PageResult<RestaurantDto>> GetAll(ListQuery query, CancellationToken ct);

    Task<RestaurantDto> GetById(Guid id, CancellationToken ct);

    Task<RestaurantDto> Create(RestaurantWriteDto body, CancellationToken ct);

    Task<RestaurantDto> Update(Guid id, RestaurantWriteDto body, CancellationToken ct);
}
