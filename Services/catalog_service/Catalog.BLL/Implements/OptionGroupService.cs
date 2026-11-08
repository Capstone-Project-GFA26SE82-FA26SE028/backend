using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Shared.Common.DTOs;
using Shared.Common.Pagination;

namespace Catalog.BLL.Implements;

public sealed class OptionGroupService(
    IOptionGroupRepository groups,
    ICatalogAccess access,
    OptionWriteExecutor writer) : IOptionGroupService
{
    public async Task<ResponseDTO> GetAll(OptionGroupQueryDTO query, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (query is null || !query.IsValid())
        {
            return new ResponseDTO("Phân trang hoặc truy vấn tìm kiếm không hợp lệ.", 400);
        }

        var page = await groups.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Lấy danh sách nhóm tùy chọn thành công.", 200, true,
            new PagedResult<OptionGroupDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã nhóm tùy chọn không hợp lệ.", 400);
        }

        var entity = await groups.GetByIdAsync(id, cancellationToken);
        return entity is null ? NotFound() : new ResponseDTO("Lấy thông tin nhóm tùy chọn thành công.", 200, true, Map(entity));
    }

    public async Task<ResponseDTO> Create(CreateOptionGroupDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        var name = OptionNames.Normalize(request?.OptionGroupName);
        if (!OptionNames.IsValid(name))
        {
            return InvalidName();
        }

        return await writer.ExecuteAsync(async () =>
        {
            if (await groups.NameExistsAsync(name, Guid.Empty, cancellationToken))
            {
                return new ResponseDTO("Tên nhóm tùy chọn đã tồn tại.", 409);
            }

            var entity = new OptionGroup
            {
                OptionGroupId = Guid.NewGuid(),
                OptionGroupName = name
            };
            groups.Create(entity);
            return new ResponseDTO("Tạo nhóm tùy chọn thành công.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdateOptionGroupDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã nhóm tùy chọn không hợp lệ.", 400);
        }

        var name = OptionNames.Normalize(request?.OptionGroupName);
        if (!OptionNames.IsValid(name))
        {
            return InvalidName();
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await groups.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            if (await groups.NameExistsAsync(name, id, cancellationToken))
            {
                return new ResponseDTO("Tên nhóm tùy chọn đã tồn tại.", 409);
            }

            entity.OptionGroupName = name;
            return new ResponseDTO("Cập nhật nhóm tùy chọn thành công.", 200, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Mã nhóm tùy chọn không hợp lệ.", 400);
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await groups.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            if (await groups.HasDependenciesAsync(id, cancellationToken))
            {
                return new ResponseDTO("Không thể xóa nhóm tùy chọn đang chứa các tùy chọn hoặc đã được gán vào món ăn.", 409);
            }

            groups.Delete(entity);
            return new ResponseDTO("Xóa nhóm tùy chọn thành công.", 200, true);
        }, cancellationToken);
    }

    private static ResponseDTO InvalidName()
    {
        return new ResponseDTO("Tên nhóm tùy chọn là bắt buộc, không quá 255 ký tự và không chứa ký tự điều khiển.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Không tìm thấy nhóm tùy chọn.", 404);
    }

    private static OptionGroupDTO Map(OptionGroup entity)
    {
        return new OptionGroupDTO
        {
            OptionGroupId = entity.OptionGroupId,
            OptionGroupName = entity.OptionGroupName
        };
    }
}
