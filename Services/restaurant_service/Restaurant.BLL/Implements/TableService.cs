using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Entity = Restaurant.DAL.Models.Table;

namespace Restaurant.BLL.Implements;

public sealed class TableService(ITableRepository repository, IAreaRepository areas,
    IManagementTransaction transaction, IManagementAccess access, ITableSessionOrderAccess waiterAccess) : ITableService
{
    public async Task<PageResult<ServiceTableDto>> GetServiceTables(ListQuery query, CancellationToken ct)
    {
        await waiterAccess.RequireStaffAsync(ct);
        ManagementValidation.Query(query);
        var page = await repository.GetServicePageAsync(query, ct);
        return new PageResult<ServiceTableDto>
        {
            Items = page.Items.Select(table => new ServiceTableDto
            {
                TableId = table.TableId,
                TableCode = table.TableCode,
                AreaId = table.AreaId,
                AreaName = table.Area.AreaName,
                RestaurantId = table.Area.RestaurantId,
                CleaningStatus = table.CleaningStatus,
                OpenSessionIds = table.SessionTables.Select(link => link.TableSessionId).Distinct().ToList()
            }).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
    }

    public async Task<PageResult<TableDto>> GetAll(ListQuery query, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ManagementValidation.Query(query);
        var page = await repository.GetPageAsync(query, ct);
        return new PageResult<TableDto>
        {
            Items = page.Items.Select(Map).ToList(),
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<TableDto> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        return Map(await Find(id, ct));
    }

    public Task<TableDto> Create(TableWriteDto body, CancellationToken ct)
    {
        return Save(null, body, ct);
    }

    public Task<TableDto> Update(Guid id, TableWriteDto body, CancellationToken ct)
    {
        return Save(id, body, ct);
    }

    private async Task<TableDto> Save(Guid? id, TableWriteDto body, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var code = ManagementValidation.Required(body.TableCode, 50, "Table code");
        if (body.Capacity <= 0 || !ValidPosition(body.PositionX) || !ValidPosition(body.PositionY))
        {
            throw new ManagementException(400, "Capacity must be a positive integer; coordinates allow at most 8 integer digits and 2 decimal places.");
        }

        var entity = await transaction.ExecuteAsync(async () =>
        {
            var area = await ActiveArea(body.AreaId, ct);
            var item = id.HasValue ? await Find(id.Value, ct) : new Entity
            {
                TableId = Guid.NewGuid(),
                AreaId = area.AreaId,
                Area = area,
                IsActive = true,
                CleaningStatus = "Clean"
            };
            if (id.HasValue)
            {
                ManagementValidation.Version(item.Version, body.Version);
                await EnsureIdle(item.TableId, ct);
                if (area.RestaurantId != item.Area.RestaurantId)
                {
                    throw new ManagementException(409, "A table cannot be moved to another restaurant.");
                }
            }

            if (await repository.CodeExistsAsync(area.RestaurantId, code, item.TableId, ct))
            {
                throw new ManagementException(409, "The table code already exists in this restaurant.");
            }

            item.AreaId = area.AreaId;
            item.Area = area;
            item.TableCode = code;
            item.Capacity = body.Capacity;
            item.PositionX = body.PositionX;
            item.PositionY = body.PositionY;
            if (!id.HasValue)
            {
                repository.Add(item);
            }

            return item;
        }, ct);
        return Map(entity);
    }

    public async Task<TableDto> AssignArea(Guid id, AssignAreaDto body, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var entity = await transaction.ExecuteAsync(async () =>
        {
            var item = await Find(id, ct);
            ManagementValidation.Version(item.Version, body.Version);
            await EnsureIdle(id, ct);
            var area = await ActiveArea(body.AreaId, ct);
            if (area.RestaurantId != item.Area.RestaurantId)
            {
                throw new ManagementException(409, "A table cannot be moved to another restaurant.");
            }

            if (await repository.CodeExistsAsync(area.RestaurantId, item.TableCode, item.TableId, ct))
            {
                throw new ManagementException(409, "The table code already exists in the destination restaurant.");
            }

            item.Area = area;
            item.AreaId = area.AreaId;
            return item;
        }, ct);
        return Map(entity);
    }

    public async Task<TableDto> SetStatus(Guid id, StatusDto body, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var entity = await transaction.ExecuteAsync(async () =>
        {
            var item = await Find(id, ct);
            ManagementValidation.Version(item.Version, body.Version);
            if (!body.IsActive)
            {
                await EnsureIdle(id, ct);
            }
            else
            {
                await ActiveArea(item.AreaId, ct);
            }

            item.IsActive = body.IsActive;
            return item;
        }, ct);
        return Map(entity);
    }

    public async Task Delete(Guid id, string version, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        await transaction.ExecuteAsync(async () =>
        {
            var item = await Find(id, ct);
            ManagementValidation.Version(item.Version, version);
            if (await repository.HasHistoryAsync(id, ct))
            {
                throw new ManagementException(409, "The table has QR codes, reservations, or service history. Deactivate it instead.");
            }

            repository.Delete(item);
            return true;
        }, ct);
    }

    private async Task<Entity> Find(Guid id, CancellationToken ct)
    {
        return await repository.GetByIdAsync(id, ct) ?? throw new ManagementException(404, "Table not found.");
    }

    private async Task<global::Restaurant.DAL.Models.Area> ActiveArea(Guid id, CancellationToken ct)
    {
        var area = await areas.GetByIdAsync(id, ct) ?? throw new ManagementException(404, "Area not found.");
        if (!area.IsActive)
        {
            throw new ManagementException(409, "The area is inactive.");
        }

        return area;
    }

    private async Task EnsureIdle(Guid id, CancellationToken ct)
    {
        if (await repository.HasOpenSessionAsync(id, ct))
        {
            throw new ManagementException(409, "The table has an unclosed service session.");
        }
    }

    private static bool ValidPosition(decimal? value)
    {
        return !value.HasValue || (value.Value >= -99999999.99m && value.Value <= 99999999.99m
            && decimal.Round(value.Value, 2) == value.Value);
    }

    internal static TableDto Map(Entity item)
    {
        return new TableDto
        {
            TableId = item.TableId,
            AreaId = item.AreaId,
            TableCode = item.TableCode,
            Capacity = item.Capacity,
            PositionX = item.PositionX,
            PositionY = item.PositionY,
            IsActive = item.IsActive,
            CleaningStatus = item.CleaningStatus,
            Version = Convert.ToBase64String(item.Version)
        };
    }
}
