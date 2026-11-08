using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Models;
using Restaurant.DAL.Interfaces;
using Entity = Restaurant.DAL.Models.Restaurant;

namespace Restaurant.BLL.Implements;

public sealed class RestaurantService(IRestaurantRepository repository, IManagementTransaction transaction, IManagementAccess access) : IRestaurantService
{
    public async Task<PageResult<RestaurantDto>> GetAll(ListQuery query, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ManagementValidation.Query(query);
        var page = await repository.GetPageAsync(query, ct);
        return new PageResult<RestaurantDto>
        {
            Items = page.Items.Select(Map).ToList(),
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<RestaurantDto> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        return Map(await repository.GetByIdAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy nhà hàng."));
    }

    public Task<RestaurantDto> Create(RestaurantWriteDto body, CancellationToken ct)
    {
        return Save(null, body, ct);
    }

    public Task<RestaurantDto> Update(Guid id, RestaurantWriteDto body, CancellationToken ct)
    {
        return Save(id, body, ct);
    }

    private async Task<RestaurantDto> Save(Guid? id, RestaurantWriteDto body, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var name = ManagementValidation.Required(body.RestaurantName, 255, "Tên nhà hàng");
        var address = ManagementValidation.Required(body.Address, 500, "Địa chỉ");
        var hotline = ManagementValidation.Required(body.Hotline, 20, "Hotline");
        var taxCode = ManagementValidation.Optional(body.TaxCode, 50, "Mã số thuế");
        var logo = ManagementValidation.Optional(body.LogoUrl, 2048, "Đường dẫn Logo");
        if (logo is not null && (!Uri.TryCreate(logo, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http")))
        {
            throw new ManagementException(400, "Logo phải là đường dẫn URL HTTP/HTTPS hợp lệ.");
        }

        var zone = ManagementValidation.Required(body.TimeZone, 100, "Múi giờ");
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ManagementException(400, "Múi giờ không hợp lệ.");
        }

        if (body.OpeningHours is null || body.OpeningHours.Count > 7
            || body.OpeningHours.Any(x => x is null || x.DayOfWeek < 0 || x.DayOfWeek > 6)
            || body.OpeningHours.Select(x => x.DayOfWeek).Distinct().Count() != body.OpeningHours.Count)
        {
            throw new ManagementException(400, "Giờ hoạt động phải từ thứ 2 đến chủ nhật (0-6) và không được trùng ngày.");
        }

        var hours = body.OpeningHours.Select(x => new OpeningHour
        {
            OpeningHourId = Guid.NewGuid(),
            DayOfWeek = x.DayOfWeek,
            OpenAt = ManagementValidation.Time(x.OpenAt),
            CloseAt = ManagementValidation.Time(x.CloseAt)
        }).ToList();
        if (hours.Any(x => x.OpenAt >= x.CloseAt))
        {
            throw new ManagementException(400, "Giờ mở cửa phải trước giờ đóng cửa trong cùng một ngày.");
        }

        var entity = await transaction.ExecuteAsync(async () =>
        {
            var item = id.HasValue
                ? await repository.GetByIdAsync(id.Value, ct) ?? throw new ManagementException(404, "Không tìm thấy nhà hàng.")
                : new Entity
                {
                    RestaurantId = Guid.NewGuid()
                };
            if (!id.HasValue)
            {
                repository.Add(item);
            }

            item.RestaurantName = name;
            item.Address = address;
            item.Hotline = hotline;
            item.TaxCode = taxCode!;
            item.LogoUrl = logo!;
            item.TimeZone = zone;
            foreach (var hour in hours)
            {
                hour.RestaurantId = item.RestaurantId;
            }

            repository.ReplaceOpeningHours(item, hours);
            return item;
        }, ct);
        return Map(entity);
    }

    internal static RestaurantDto Map(Entity item)
    {
        return new RestaurantDto
        {
            RestaurantId = item.RestaurantId,
            RestaurantName = item.RestaurantName,
            Address = item.Address,
            Hotline = item.Hotline,
            TaxCode = item.TaxCode,
            LogoUrl = item.LogoUrl,
            TimeZone = item.TimeZone,
            OpeningHours = item.OpeningHours.OrderBy(x => x.DayOfWeek).Select(x => new OpeningHourDto
            {
                DayOfWeek = x.DayOfWeek,
                OpenAt = x.OpenAt.ToString("HH:mm"),
                CloseAt = x.CloseAt.ToString("HH:mm")
            }).ToList()
        };
    }
}
