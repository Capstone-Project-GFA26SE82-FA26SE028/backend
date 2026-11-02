using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed class KitchenStationService(
    IKitchenStationRepository repository, IOrderingTransaction transaction,
    IKitchenStationAccess access) : IKitchenStationService
{
    public async Task<KitchenStationDTO> Create(KitchenStationWriteDTO request, CancellationToken ct)
    {
        await access.RequireAsync(true, ct);
        var (code, name) = Validate(request);
        return await transaction.ExecuteAsync(async () =>
        {
            await Unique(code, Guid.Empty, ct);
            var station = new KitchenStation
            {
                KitchenStationId = Guid.NewGuid(),
                KitchenStationCode = code,
                KitchenStationName = name,
                IsActive = true
            };
            await repository.CreateAsync(station, ct);
            return Map(station);
        }, ct);
    }

    public async Task<KitchenStationPageDTO> GetAll(
        int page, int pageSize, string? search, bool? isActive, CancellationToken ct)
    {
        await access.RequireAsync(false, ct);
        if (page < 1 || pageSize is < 1 or > 100
            || (long)(page - 1) * pageSize > int.MaxValue || search?.Length > 255)
        {
            throw new OrderingException(400, "Phân trang hoặc từ khóa tìm kiếm không hợp lệ.");
        }
        var result = await repository.GetPageAsync(page, pageSize, search?.Trim(), isActive, ct);
        return new KitchenStationPageDTO
        {
            Items = result.Items.Select(Map).ToList(),
            TotalCount = result.Total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<KitchenStationDTO> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(false, ct);
        return Map(await Find(id, ct));
    }

    public async Task<KitchenStationDTO> Update(Guid id, KitchenStationWriteDTO request, CancellationToken ct)
    {
        await access.RequireAsync(true, ct);
        var (code, name) = Validate(request);
        return await transaction.ExecuteAsync(async () =>
        {
            var station = await Find(id, ct);
            await Unique(code, id, ct);
            station.KitchenStationCode = code;
            station.KitchenStationName = name;
            return Map(station);
        }, ct);
    }

    public async Task<KitchenStationDTO> SetStatus(Guid id, bool isActive, CancellationToken ct)
    {
        await access.RequireAsync(true, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var station = await Find(id, ct);
            station.IsActive = isActive;
            return Map(station);
        }, ct);
    }

    private async Task<KitchenStation> Find(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new OrderingException(400, "Mã trạm bếp không hợp lệ.");
        }
        return await repository.GetByIdAsync(id, ct)
            ?? throw new OrderingException(404, "Không tìm thấy trạm bếp.");
    }

    private async Task Unique(string code, Guid id, CancellationToken ct)
    {
        if (await repository.CodeExistsAsync(code, id, ct))
        {
            throw new OrderingException(409, "Mã trạm bếp đã tồn tại.");
        }
    }

    private static (string Code, string Name) Validate(KitchenStationWriteDTO request)
    {
        var code = request.KitchenStationCode?.Trim().ToUpperInvariant() ?? "";
        var name = request.KitchenStationName?.Trim() ?? "";
        if (code.Length is < 1 or > 50 || name.Length is < 1 or > 255
            || code.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            || name.Any(char.IsControl))
        {
            throw new OrderingException(400, "Mã phải từ 1-50 ký tự chữ cái, số, gạch ngang hoặc gạch dưới; tên phải từ 1-255 ký tự.");
        }
        return (code, name);
    }

    private static KitchenStationDTO Map(KitchenStation station)
    {
        return new KitchenStationDTO
        {
            KitchenStationId = station.KitchenStationId,
            KitchenStationCode = station.KitchenStationCode,
            KitchenStationName = station.KitchenStationName,
            IsActive = station.IsActive
        };
    }
}
