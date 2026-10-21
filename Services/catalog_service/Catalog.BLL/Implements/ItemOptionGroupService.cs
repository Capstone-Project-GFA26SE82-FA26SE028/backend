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
            return new ResponseDTO("Item not found.", 404);
        }

        var entities = await links.GetAllAsync(itemId, cancellationToken);
        return new ResponseDTO("Item option groups retrieved successfully.", 200, true, entities.Select(Map).ToArray());
    }

    public async Task<ResponseDTO> GetById(Guid itemId, Guid groupId, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (itemId == Guid.Empty || groupId == Guid.Empty)
        {
            return InvalidId();
        }

        var entity = await links.GetByIdAsync(itemId, groupId, cancellationToken);
        return entity is null ? NotFound() : new ResponseDTO("Item option group retrieved successfully.", 200, true, Map(entity));
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
                return new ResponseDTO("Item not found.", 404);
            }

            var group = await groups.GetByIdAsync(request.OptionGroupId, cancellationToken);
            if (group is null)
            {
                return new ResponseDTO("Option group not found.", 404);
            }

            if (await links.GetByIdAsync(itemId, request.OptionGroupId, cancellationToken) is not null)
            {
                return new ResponseDTO("The option group is already assigned to this item.", 409);
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
            return new ResponseDTO("Item option group created successfully.", 201, true, Map(entity));
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
            return new ResponseDTO("Item option group updated successfully.", 200, true, Map(entity));
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
            return new ResponseDTO("Item option group deleted successfully.", 200, true);
        }, cancellationToken);
    }

    private static bool ValidLimits(int? minimum, int? maximum)
    {
        return minimum.HasValue && maximum.HasValue && minimum >= 0 && maximum >= 1 && minimum <= maximum;
    }

    private static ResponseDTO InvalidLimits()
    {
        return new ResponseDTO("MinSelection and MaxSelection are required; 0 <= MinSelection <= MaxSelection and MaxSelection >= 1.", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Valid item and option group IDs are required.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("The option group is not assigned to this item.", 404);
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
