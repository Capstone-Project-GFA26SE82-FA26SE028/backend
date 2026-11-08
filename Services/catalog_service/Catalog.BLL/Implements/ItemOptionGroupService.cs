using Catalog.BLL.Interfaces;
using Catalog.Common.DTOs;
using Catalog.DAL.Interfaces;
using Catalog.DAL.Models;
using Shared.Common.DTOs;

namespace Catalog.BLL.Implements;

public sealed class ItemOptionGroupService(
    IItemOptionGroupRepository links,
    IItemRepository items,
    IOptionGroupRepository groups,
    ICatalogAccess access,
    OptionWriteExecutor writer) : IItemOptionGroupService
{
    public async Task<ResponseDTO> GetAll(Guid itemId, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty)
        {
            return InvalidId();
        }

        if (await items.GetByIdAsync(itemId, cancellationToken) is null)
        {
            return new ResponseDTO("Không tìm thấy món ăn.", 404);
        }

        var entities = await links.GetAllAsync(itemId, cancellationToken);
        return new ResponseDTO("Lấy danh sách nhóm tùy chọn của món ăn thành công.", 200, true, entities.Select(Map).ToArray());
    }

    public async Task<ResponseDTO> GetById(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty || groupId == Guid.Empty)
        {
            return InvalidId();
        }

        var entity = await links.GetByIdAsync(itemId, groupId, cancellationToken);
        return entity is null ? NotFound() : new ResponseDTO("Lấy thông tin nhóm tùy chọn của món ăn thành công.", 200, true, Map(entity));
    }

    public async Task<ResponseDTO> Create(Guid itemId, CreateItemOptionGroupDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty || request is null || request.OptionGroupId == Guid.Empty)
        {
            return InvalidId();
        }

        if (!ValidLimits(request.MinSelection, request.MaxSelection))
        {
            return InvalidLimits();
        }

        return await writer.ExecuteAsync(async () =>
        {
            if (await items.GetByIdAsync(itemId, cancellationToken) is null)
            {
                return new ResponseDTO("Không tìm thấy món ăn.", 404);
            }

            var group = await groups.GetByIdAsync(request.OptionGroupId, cancellationToken);
            if (group is null)
            {
                return new ResponseDTO("Không tìm thấy nhóm tùy chọn.", 404);
            }

            if (await links.GetByIdAsync(itemId, request.OptionGroupId, cancellationToken) is not null)
            {
                return new ResponseDTO("Nhóm tùy chọn đã được gán cho món ăn này.", 409);
            }

            var entity = new ItemOptionGroup
            {
                ItemId = itemId,
                OptionGroupId = group.OptionGroupId,
                OptionGroup = group,
                MinSelection = request.MinSelection!.Value,
                MaxSelection = request.MaxSelection!.Value
            };
            links.Create(entity);
            return new ResponseDTO("Gán nhóm tùy chọn cho món ăn thành công.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(
        Guid itemId, Guid groupId, UpdateItemOptionGroupDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty || groupId == Guid.Empty)
        {
            return InvalidId();
        }

        if (request is null || !ValidLimits(request.MinSelection, request.MaxSelection))
        {
            return InvalidLimits();
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await links.GetByIdAsync(itemId, groupId, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            entity.MinSelection = request.MinSelection!.Value;
            entity.MaxSelection = request.MaxSelection!.Value;
            return new ResponseDTO("Cập nhật nhóm tùy chọn của món ăn thành công.", 200, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty || groupId == Guid.Empty)
        {
            return InvalidId();
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await links.GetByIdAsync(itemId, groupId, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            links.Delete(entity);
            return new ResponseDTO("Hủy gán nhóm tùy chọn khỏi món ăn thành công.", 200, true);
        }, cancellationToken);
    }

    private static bool ValidLimits(int? minimum, int? maximum)
    {
        return minimum.HasValue && maximum.HasValue && minimum >= 0 && maximum >= 1 && minimum <= maximum;
    }

    private static ResponseDTO InvalidLimits()
    {
        return new ResponseDTO("MinSelection và MaxSelection là bắt buộc; 0 <= MinSelection <= MaxSelection và MaxSelection >= 1.", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Yêu cầu mã món ăn và mã nhóm tùy chọn hợp lệ.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Nhóm tùy chọn chưa được gán cho món ăn này.", 404);
    }

    private static ItemOptionGroupDTO Map(ItemOptionGroup entity)
    {
        return new ItemOptionGroupDTO
        {
            ItemId = entity.ItemId,
            OptionGroupId = entity.OptionGroupId,
            OptionGroupName = entity.OptionGroup.OptionGroupName,
            MinSelection = entity.MinSelection,
            MaxSelection = entity.MaxSelection
        };
    }
}
