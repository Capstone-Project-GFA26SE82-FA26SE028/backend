using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class CashRegisterService(ICashRegisterRepository registers, IUnitOfWork unitOfWork,
    IBillingAccess access, IOptions<CashRegisterAccessSettings> settings) : ICashRegisterService
{
    public async Task<CashRegisterDTO> Create(CreateCashRegisterDTO request, CancellationToken cancellationToken)
    {
        var actor = await access.GetCurrentScopeAsync(null, cancellationToken);
        RequireRole(actor, allowCashier: false);
        if (request is null || request.RestaurantId == Guid.Empty)
        {
            throw new BillingException(400, "A valid restaurant ID is required.");
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 50 || code.Any(character => character < ' ' || character > '~'))
        {
            throw new BillingException(400, "The register code must contain 1 to 50 printable ASCII characters.");
        }

        RequireBranch(actor, request.RestaurantId);
        var verified = await access.GetCurrentScopeAsync(request.RestaurantId, cancellationToken);
        RequireRole(verified, allowCashier: false);
        RequireBranch(verified, request.RestaurantId);

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await registers.LockRestaurantAsync(request.RestaurantId, cancellationToken))
            {
                throw new BillingException(409, "Cash register creation is busy for this restaurant. Retry the request.");
            }

            if (await registers.CodeExistsAsync(request.RestaurantId, code, cancellationToken))
            {
                throw new BillingException(409, "The register code already exists in this restaurant.");
            }

            var register = new CashRegister
            {
                CashRegisterId = Guid.NewGuid(),
                RestaurantId = request.RestaurantId,
                Code = code
            };
            await registers.AddAsync(register);
            return Map(register);
        }, cancellationToken);
    }

    public async Task<PagedResult<CashRegisterDTO>> GetAll(CashRegisterQueryDTO query, CancellationToken cancellationToken)
    {
        var actor = await access.GetCurrentScopeAsync(null, cancellationToken);
        RequireRole(actor, allowCashier: true);
        if (query is null || !new PaginationRequest
        {
            Page = query.Page,
            PageSize = query.PageSize
        }.IsValid() || query.RestaurantId == Guid.Empty || query.Search?.Length > 50
            || query.Search?.Any(char.IsControl) == true)
        {
            throw new BillingException(400, "Provide valid pagination, a valid optional restaurant ID, and a search of at most 50 characters.");
        }

        if (query.RestaurantId.HasValue)
        {
            RequireBranch(actor, query.RestaurantId.Value);
        }

        var result = await registers.GetPageAsync(query, actor.IsAdministrator ? null : actor.RestaurantIds, cancellationToken);
        return new PagedResult<CashRegisterDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<CashRegisterDTO> GetById(Guid id, CancellationToken cancellationToken)
    {
        var actor = await access.GetCurrentScopeAsync(null, cancellationToken);
        RequireRole(actor, allowCashier: true);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid cash register ID is required.");
        }

        var register = await registers.GetByIdAsync(id, cancellationToken)
            ?? throw new BillingException(404, "Cash register not found.");
        RequireBranch(actor, register.RestaurantId);
        return Map(register);
    }

    private void RequireRole(BillingAccessScope actor, bool allowCashier)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        if (actor.IsAdministrator)
        {
            return;
        }

        var manager = settings.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var cashier = allowCashier && settings.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (!manager && !cashier)
        {
            throw new BillingException(403, "Your role cannot perform this cash register operation.");
        }

        if (actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "An active restaurant assignment is required.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "You are not assigned to this restaurant.");
        }
    }

    private static CashRegisterDTO Map(CashRegister register)
    {
        return new CashRegisterDTO
        {
            CashRegisterId = register.CashRegisterId,
            RestaurantId = register.RestaurantId,
            Code = register.Code
        };
    }
}
