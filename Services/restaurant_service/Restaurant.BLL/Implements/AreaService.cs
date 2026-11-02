using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class AreaService(IAreaRepository repository, IRestaurantRepository restaurants,
    IManagementTransaction transaction, IManagementAccess access) : IAreaService
{
    public async Task<PageResult<AreaDto>> GetAll(ListQuery query, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ManagementValidation.Query(query);
        var page = await repository.GetPageAsync(query, ct);
        return new PageResult<AreaDto>
        {
            Items = page.Items.Select(Map).ToList(),
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<AreaDto> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        return Map(await Find(id, ct));
    }

    public Task<AreaDto> Create(AreaWriteDto body, CancellationToken ct)
    {
        return Save(null, body, ct);
    }

    public Task<AreaDto> Update(Guid id, AreaWriteDto body, CancellationToken ct)
    {
        return Save(id, body, ct);
    }

    private async Task<AreaDto> Save(Guid? id, AreaWriteDto body, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var code = ManagementValidation.Required(body.AreaCode, 50, "Mã khu vực");
        var name = ManagementValidation.Required(body.AreaName, 100, "Tên khu vực");
        var serviceType = ManagementValidation.Required(body.ServiceType, 50, "ServiceType");
        var spaceType = ManagementValidation.Required(body.SpaceType, 50, "SpaceType");
        var entity = await transaction.ExecuteAsync(async () =>
        {
            if (await restaurants.GetByIdAsync(body.RestaurantId, ct) is null)
            {
                throw new ManagementException(404, "Không tìm thấy nhà hàng.");
            }

            var item = id.HasValue ? await Find(id.Value, ct) : new Area
            {
                AreaId = Guid.NewGuid(),
                RestaurantId = body.RestaurantId,
                IsActive = true
            };
            if (item.RestaurantId != body.RestaurantId)
            {
                throw new ManagementException(409, "Không thể chuyển khu vực sang nhà hàng khác.");
            }

            if (await repository.CodeExistsAsync(body.RestaurantId, code, item.AreaId, ct))
            {
                throw new ManagementException(409, "Mã khu vực đã tồn tại trong nhà hàng này.");
            }

            item.AreaCode = code;
            item.AreaName = name;
            item.ServiceType = serviceType;
            item.SpaceType = spaceType;
            if (!id.HasValue)
            {
                repository.Add(item);
            }

            return item;
        }, ct);
        return Map(entity);
    }

    public async Task<AreaDto> SetStatus(Guid id, bool active, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        return Map(await transaction.ExecuteAsync(async () =>
        {
            var entity = await Find(id, ct);
            if (!active && await repository.HasOpenSessionsAsync(id, ct))
            {
                throw new ManagementException(409, "Khu vực đang có phiên phục vụ chưa đóng.");
            }

            entity.IsActive = active;
            return entity;
        }, ct));
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        await transaction.ExecuteAsync(async () =>
        {
            var entity = await Find(id, ct);
            if (await repository.HasTablesAsync(id, ct))
            {
                throw new ManagementException(409, "Khu vực vẫn còn bàn. Hãy chuyển bàn hoặc ngừng kích hoạt khu vực.");
            }

            repository.Delete(entity);
            return true;
        }, ct);
    }

    private async Task<Area> Find(Guid id, CancellationToken ct)
    {
        return await repository.GetByIdAsync(id, ct) ?? throw new ManagementException(404, "Không tìm thấy khu vực.");
    }

    internal static AreaDto Map(Area item)
    {
        return new AreaDto
        {
            AreaId = item.AreaId,
            RestaurantId = item.RestaurantId,
            AreaCode = item.AreaCode,
            AreaName = item.AreaName,
            ServiceType = item.ServiceType,
            SpaceType = item.SpaceType,
            IsActive = item.IsActive
        };
    }
}
