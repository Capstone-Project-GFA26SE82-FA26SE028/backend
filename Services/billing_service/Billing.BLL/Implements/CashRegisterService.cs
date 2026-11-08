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
            throw new BillingException(400, "Yêu cầu ID nhà hàng hợp lệ.");
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 50 || code.Any(character => character < ' ' || character > '~'))
        {
            throw new BillingException(400, "Mã quầy thu ngân phải gồm từ 1 đến 50 ký tự ASCII có thể hiển thị.");
        }

        RequireBranch(actor, request.RestaurantId);
        var verified = await access.GetCurrentScopeAsync(request.RestaurantId, cancellationToken);
        RequireRole(verified, allowCashier: false);
        RequireBranch(verified, request.RestaurantId);

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await registers.LockRestaurantAsync(request.RestaurantId, cancellationToken))
            {
                throw new BillingException(409, "Thao tác tạo quầy thu ngân tại nhà hàng này đang bận. Vui lòng thử lại.");
            }

            if (await registers.CodeExistsAsync(request.RestaurantId, code, cancellationToken))
            {
                throw new BillingException(409, "Mã quầy thu ngân đã tồn tại trong nhà hàng này.");
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
            throw new BillingException(400, "Vui lòng cung cấp thông tin phân trang hợp lệ, ID nhà hàng hợp lệ nếu có và từ khóa tìm kiếm tối đa 50 ký tự.");
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
            throw new BillingException(400, "Yêu cầu ID quầy thu ngân hợp lệ.");
        }

        var register = await registers.GetByIdAsync(id, cancellationToken)
            ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
        RequireBranch(actor, register.RestaurantId);
        return Map(register);
    }

    private void RequireRole(BillingAccessScope actor, bool allowCashier)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        if (actor.IsAdministrator)
        {
            return;
        }

        var manager = settings.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var cashier = allowCashier && settings.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (!manager && !cashier)
        {
            throw new BillingException(403, "Vai trò của bạn không có quyền thực hiện thao tác này tại quầy thu ngân.");
        }

        if (actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "Yêu cầu phân công nhà hàng đang hoạt động.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Bạn chưa được phân công tại nhà hàng này.");
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
