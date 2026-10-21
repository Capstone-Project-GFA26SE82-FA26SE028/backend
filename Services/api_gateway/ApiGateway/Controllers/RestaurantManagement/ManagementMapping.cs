using System.Globalization;
using Backend.Contracts.Restaurant;
using ApiGateway.DTOs.Restaurant;
using ApiGateway.Exceptions;

namespace ApiGateway.Controllers.RestaurantManagement;

internal static class ManagementMapping
{
    public static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new GatewayRequestException(400, "ID must be a non-empty GUID.");
        }

        return id;
    }

    private static decimal? Decimal(string value, bool present)
    {
        if (!present)
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number))
        {
            throw new GatewayRequestException(400, "Invalid coordinates.");
        }

        return number;
    }

    public static ListQuery ToDto(ListRequest value)
    {
        return new ListQuery
        {
            RestaurantId = value.HasRestaurantId ? Id(value.RestaurantId) : null,
            AreaId = value.HasAreaId ? Id(value.AreaId) : null,
            IsActive = value.HasIsActive ? value.IsActive : null,
            Search = value.Search,
            Page = value.Page,
            PageSize = value.PageSize
        };
    }

    public static ListRequest ToWire(ListQuery value)
    {
        var result = new ListRequest
        {
            Search = value.Search ?? "",
            Page = value.Page,
            PageSize = value.PageSize
        };
        if (value.RestaurantId.HasValue)
        {
            result.RestaurantId = value.RestaurantId.Value.ToString();
        }

        if (value.AreaId.HasValue)
        {
            result.AreaId = value.AreaId.Value.ToString();
        }

        if (value.IsActive.HasValue)
        {
            result.IsActive = value.IsActive.Value;
        }

        return result;
    }

    public static RestaurantDto ToDto(RestaurantInfo value)
    {
        return new RestaurantDto
        {
            RestaurantId = string.IsNullOrEmpty(value.RestaurantId) ? Guid.Empty : Id(value.RestaurantId),
            RestaurantName = value.RestaurantName,
            Address = value.Address,
            Hotline = value.Hotline,
            TaxCode = value.HasTaxCode ? value.TaxCode : null,
            LogoUrl = value.HasLogoUrl ? value.LogoUrl : null,
            TimeZone = value.TimeZone,
            OpeningHours = value.OpeningHours.Select(x => new OpeningHourDto
            {
                DayOfWeek = x.DayOfWeek,
                OpenAt = x.OpenAt,
                CloseAt = x.CloseAt
            }).ToList()
        };
    }

    public static RestaurantInfo ToWire(RestaurantDto value)
    {
        var result = new RestaurantInfo
        {
            RestaurantId = value.RestaurantId == Guid.Empty ? "" : value.RestaurantId.ToString(),
            RestaurantName = value.RestaurantName ?? "",
            Address = value.Address ?? "",
            Hotline = value.Hotline ?? "",
            TimeZone = value.TimeZone ?? ""
        };
        if (value.TaxCode is not null)
        {
            result.TaxCode = value.TaxCode;
        }

        if (value.LogoUrl is not null)
        {
            result.LogoUrl = value.LogoUrl;
        }

        result.OpeningHours.AddRange(value.OpeningHours.Select(x => new OpeningHourInfo
        {
            DayOfWeek = x.DayOfWeek,
            OpenAt = x.OpenAt,
            CloseAt = x.CloseAt
        }));
        return result;
    }

    public static AreaDto ToDto(AreaInfo value)
    {
        return new AreaDto
        {
            AreaId = string.IsNullOrEmpty(value.AreaId) ? Guid.Empty : Id(value.AreaId),
            RestaurantId = Id(value.RestaurantId),
            AreaCode = value.AreaCode,
            AreaName = value.AreaName,
            ServiceType = value.ServiceType,
            SpaceType = value.SpaceType,
            IsActive = value.IsActive
        };
    }

    public static AreaInfo ToWire(AreaDto value)
    {
        var result = new AreaInfo
        {
            AreaId = value.AreaId == Guid.Empty ? "" : value.AreaId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            AreaCode = value.AreaCode ?? "",
            AreaName = value.AreaName ?? "",
            ServiceType = value.ServiceType ?? "",
            SpaceType = value.SpaceType ?? "",
            IsActive = value.IsActive
        };
        return result;
    }

    public static TableDto ToDto(TableInfo value)
    {
        return new TableDto
        {
            TableId = string.IsNullOrEmpty(value.TableId) ? Guid.Empty : Id(value.TableId),
            AreaId = Id(value.AreaId),
            TableCode = value.TableCode,
            Capacity = value.Capacity,
            PositionX = Decimal(value.PositionX, value.HasPositionX),
            PositionY = Decimal(value.PositionY, value.HasPositionY),
            IsActive = value.IsActive,
            CleaningStatus = value.CleaningStatus,
            Version = value.Version
        };
    }

    public static TableInfo ToWire(TableDto value)
    {
        var result = new TableInfo
        {
            TableId = value.TableId == Guid.Empty ? "" : value.TableId.ToString(),
            AreaId = value.AreaId.ToString(),
            TableCode = value.TableCode ?? "",
            Capacity = value.Capacity,
            IsActive = value.IsActive,
            CleaningStatus = value.CleaningStatus ?? "",
            Version = value.Version ?? ""
        };
        if (value.PositionX is not null)
        {
            result.PositionX = value.PositionX.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (value.PositionY is not null)
        {
            result.PositionY = value.PositionY.Value.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }
}
