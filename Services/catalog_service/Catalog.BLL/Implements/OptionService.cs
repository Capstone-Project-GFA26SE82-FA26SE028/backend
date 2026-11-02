using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.Common.Settings;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.BLL.Implements;

public sealed class OptionService(
    IOptionRepository options,
    IOptionGroupRepository groups,
    ICatalogAccess access,
    OptionWriteExecutor writer) : IOptionService
{
    public async Task<ResponseDTO> GetAll(OptionQueryDTO query, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (query is null || !query.IsValid())
        {
            return new ResponseDTO("Phân trang hoặc bộ lọc tùy chọn không hợp lệ.", 400);
        }

        if (query.OptionGroupId.HasValue
            && await groups.GetByIdAsync(query.OptionGroupId.Value, cancellationToken) is null)
        {
            return new ResponseDTO("Không tìm thấy nhóm tùy chọn.", 404);
        }

        var page = await options.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Lấy danh sách tùy chọn thành công.", 200, true,
            new PagedResult<OptionDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        var entity = await options.GetByIdAsync(id, cancellationToken);
        return entity is null ? NotFound() : new ResponseDTO("Lấy thông tin tùy chọn thành công.", 200, true, Map(entity));
    }

    public async Task<ResponseDTO> Create(CreateOptionDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        var name = OptionNames.Normalize(request?.OptionName);
        if (request is null || request.OptionGroupId == Guid.Empty
            || !OptionNames.IsValid(name) || !PriceRules.TryAmount(request.ExtraPrice, out var amount))
        {
            return InvalidFields();
        }

        return await writer.ExecuteAsync(async () =>
        {
            if (await groups.GetByIdAsync(request.OptionGroupId, cancellationToken) is null)
            {
                return new ResponseDTO("Không tìm thấy nhóm tùy chọn.", 404);
            }

            if (await options.NameExistsAsync(request.OptionGroupId, name, Guid.Empty, cancellationToken))
            {
                return DuplicateName();
            }

            var entity = new Option
            {
                OptionId = Guid.NewGuid(),
                OptionGroupId = request.OptionGroupId,
                OptionName = name,
                ExtraPrice = amount,
                Availability = ItemAvailability.Inactive
            };
            options.Create(entity);
            return new ResponseDTO("Tạo tùy chọn thành công.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdateOptionDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        var name = OptionNames.Normalize(request?.OptionName);
        if (!OptionNames.IsValid(name) || !PriceRules.TryAmount(request?.ExtraPrice, out var amount))
        {
            return InvalidFields();
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await options.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            if (await options.NameExistsAsync(entity.OptionGroupId, name, id, cancellationToken))
            {
                return DuplicateName();
            }

            entity.OptionName = name;
            entity.ExtraPrice = amount;
            return new ResponseDTO("Cập nhật tùy chọn thành công.", 200, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> SetStatus(Guid id, SetOptionStatusDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        if (request?.Availability is not (ItemAvailability.Available or ItemAvailability.OutOfStock or ItemAvailability.Inactive))
        {
            return new ResponseDTO("Trạng thái phải là Available, OutOfStock hoặc Inactive.", 400);
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await options.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            entity.Availability = request.Availability;
            return new ResponseDTO("Cập nhật trạng thái tùy chọn thành công.", 200, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return InvalidId();
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await options.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            if (await options.HasDependenciesAsync(id, cancellationToken))
            {
                return new ResponseDTO("Không thể xóa tùy chọn đã có công thức hoặc thuộc nhóm đã gán cho món. Hãy chuyển sang Inactive.", 409);
            }

            options.Delete(entity);
            return new ResponseDTO("Xóa tùy chọn thành công.", 200, true);
        }, cancellationToken);
    }

    private static ResponseDTO InvalidFields()
    {
        return new ResponseDTO("Yêu cầu mã nhóm hợp lệ, tên từ 1 đến 255 ký tự và giá cộng thêm không âm theo định dạng decimal(18,2).", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Mã tùy chọn không hợp lệ.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Không tìm thấy tùy chọn.", 404);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("Tên tùy chọn đã tồn tại trong nhóm này.", 409);
    }

    private static OptionDTO Map(Option entity)
    {
        return new OptionDTO
        {
            OptionId = entity.OptionId,
            OptionGroupId = entity.OptionGroupId,
            OptionName = entity.OptionName,
            ExtraPrice = entity.ExtraPrice,
            Availability = entity.Availability
        };
    }
}
