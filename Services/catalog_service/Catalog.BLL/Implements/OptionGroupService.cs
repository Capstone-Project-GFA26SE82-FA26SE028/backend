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
            return new ResponseDTO("Invalid pagination or search query.", 400);
        }

        var page = await groups.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Option groups retrieved successfully.", 200, true,
            new PagedResult<OptionGroupDTO>(page.Items.Select(Map).ToArray(), page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ResponseDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid option group ID.", 400);
        }

        var entity = await groups.GetByIdAsync(id, cancellationToken);
        return entity is null ? NotFound() : new ResponseDTO("Option group retrieved successfully.", 200, true, Map(entity));
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
                return new ResponseDTO("The option group name already exists.", 409);
            }

            var entity = new OptionGroup
            {
                OptionGroupId = Guid.NewGuid(),
                OptionGroupName = name
            };
            groups.Create(entity);
            return new ResponseDTO("Option group created successfully.", 201, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Update(Guid id, UpdateOptionGroupDTO request, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid option group ID.", 400);
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
                return new ResponseDTO("The option group name already exists.", 409);
            }

            entity.OptionGroupName = name;
            return new ResponseDTO("Option group updated successfully.", 200, true, Map(entity));
        }, cancellationToken);
    }

    public async Task<ResponseDTO> Delete(Guid id, CancellationToken cancellationToken)
    {
        await access.RequireMenuManagerAsync(cancellationToken);
        if (id == Guid.Empty)
        {
            return new ResponseDTO("Invalid option group ID.", 400);
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
                return new ResponseDTO("Cannot delete an option group that contains options or is assigned to an item.", 409);
            }

            groups.Delete(entity);
            return new ResponseDTO("Option group deleted successfully.", 200, true);
        }, cancellationToken);
    }

    private static ResponseDTO InvalidName()
    {
        return new ResponseDTO("The option group name is required, must not exceed 255 characters, and must not contain control characters.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Option group not found.", 404);
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
