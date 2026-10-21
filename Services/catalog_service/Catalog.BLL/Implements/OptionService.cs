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
            return new ResponseDTO("Invalid pagination or option filter.", 400);
        }

        if (query.OptionGroupId.HasValue
            && await groups.GetByIdAsync(query.OptionGroupId.Value, cancellationToken) is null)
        {
            return new ResponseDTO("Option group not found.", 404);
        }

        var page = await options.GetPageAsync(query, cancellationToken);
        return new ResponseDTO("Options retrieved successfully.", 200, true,
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
        return entity is null ? NotFound() : new ResponseDTO("Option retrieved successfully.", 200, true, Map(entity));
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
                return new ResponseDTO("Option group not found.", 404);
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
            return new ResponseDTO("Option created successfully.", 201, true, Map(entity));
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
            return new ResponseDTO("Option updated successfully.", 200, true, Map(entity));
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
            return new ResponseDTO("Availability must be Available, OutOfStock, or Inactive.", 400);
        }

        return await writer.ExecuteAsync(async () =>
        {
            var entity = await options.GetByIdAsync(id, cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            entity.Availability = request.Availability;
            return new ResponseDTO("Option status updated successfully.", 200, true, Map(entity));
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
                return new ResponseDTO("Cannot delete an option with recipes or whose group is assigned to an item. Set it to Inactive instead.", 409);
            }

            options.Delete(entity);
            return new ResponseDTO("Option deleted successfully.", 200, true);
        }, cancellationToken);
    }

    private static ResponseDTO InvalidFields()
    {
        return new ResponseDTO("A valid group ID (on creation), a name of 1 to 255 characters without control characters, and a non-negative extra price fitting decimal(18,2) are required.", 400);
    }

    private static ResponseDTO InvalidId()
    {
        return new ResponseDTO("Invalid option ID.", 400);
    }

    private static ResponseDTO NotFound()
    {
        return new ResponseDTO("Option not found.", 404);
    }

    private static ResponseDTO DuplicateName()
    {
        return new ResponseDTO("The option name already exists in this group.", 409);
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
