using System.Data.SqlTypes;
using System.Globalization;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class TaxFeePolicyService(ITaxFeePolicyRepository policies, IUnitOfWork unitOfWork,
    IBillingAccess access, TimeProvider clock) : ITaxFeePolicyService
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    public async Task<TaxFeePolicyDTO> Create(CreateTaxFeePolicyDTO request, CancellationToken ct)
    {
        var actor = await RequireAdministrator(ct);
        if (request is null || request.VatRate is not decimal vatRate || !ValidRate(vatRate)
            || request.ServiceRate is not decimal serviceRate || !ValidRate(serviceRate)
            || request.ServiceEnabled is not bool serviceEnabled)
        {
            throw new BillingException(400, "Cung cấp tỷ lệ VAT và phí dịch vụ từ 0 đến 1 có tối đa 6 chữ số thập phân, cùng giá trị serviceEnabled.");
        }

        DateTime? requestedUtc = null;
        if (request.EffectiveFrom is not null)
        {
            if (!DateTimeOffset.TryParseExact(request.EffectiveFrom, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp)
                || timestamp.UtcDateTime < SqlDateTime.MinValue.Value || timestamp.UtcDateTime > SqlDateTime.MaxValue.Value)
            {
                throw new BillingException(400, "Dùng effectiveFrom định dạng ISO 8601 có múi giờ UTC trong phạm vi datetime SQL, hoặc bỏ trống để có hiệu lực ngay.");
            }

            requestedUtc = timestamp.UtcDateTime;
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await policies.LockCreateAsync(ct))
            {
                throw new BillingException(409, "Tạo chính sách thuế đang bận. Vui lòng thử lại yêu cầu.");
            }

            var currentActor = await RequireAdministrator(ct);
            if (currentActor != actor)
            {
                throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var effective = DateTime.SpecifyKind(new SqlDateTime(requestedUtc ?? now).Value, DateTimeKind.Utc);
            if (requestedUtc.HasValue)
            {
                if (requestedUtc.Value <= now || effective <= now)
                {
                    throw new BillingException(400, "effectiveFrom phải ở tương lai theo độ chính xác SQL datetime, hoặc bỏ trống để có hiệu lực ngay.");
                }
            }
            else if (effective > now)
            {
                effective = DateTime.SpecifyKind(new SqlDateTime(effective.AddMilliseconds(-3)).Value, DateTimeKind.Utc);
            }

            if (await policies.EffectiveFromExistsAsync(effective, ct))
            {
                throw new BillingException(409, "Đã tồn tại chính sách thuế tại thời điểm hiệu lực này.");
            }

            var highestVersion = await policies.GetHighestVersionAsync(ct);
            if (highestVersion.HasValue && (highestVersion.Value < 1 || highestVersion.Value == int.MaxValue))
            {
                throw new BillingException(409, "Các phiên bản chính sách thuế hiện tại cần được xem xét trước khi tạo phiên bản mới.");
            }

            var policy = new TaxFeePolicy
            {
                TaxFeePolicyId = Guid.NewGuid(),
                Version = (highestVersion ?? 0) + 1,
                VatRate = vatRate,
                ServiceRate = serviceRate,
                ServiceEnabled = serviceEnabled,
                EffectiveFrom = effective
            };
            await policies.AddAsync(policy, ct);
            return Map(policy);
        }, ct);
    }

    public async Task<PagedResult<TaxFeePolicyDTO>> GetAll(TaxFeePolicyQueryDTO query, CancellationToken ct)
    {
        await RequireAdministrator(ct);
        if (query is null || !new PaginationRequest
        {
            Page = query.Page,
            PageSize = query.PageSize
        }.IsValid())
        {
            throw new BillingException(400, "Cung cấp phân trang hợp lệ với kích thước trang từ 1 đến 100.");
        }

        var result = await policies.GetPageAsync(query, ct);
        return new PagedResult<TaxFeePolicyDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<TaxFeePolicyDTO> GetById(Guid id, CancellationToken ct)
    {
        await RequireAdministrator(ct);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Mã chính sách thuế không hợp lệ hoặc bị trống.");
        }

        var policy = await policies.GetByIdAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy chính sách thuế.");
        return Map(policy);
    }

    private async Task<Guid> RequireAdministrator(CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        if (!actor.IsAdministrator)
        {
            throw new BillingException(403, "Chỉ Quản trị viên hệ thống mới có thể quản lý chính sách thuế và phí dịch vụ.");
        }

        return actor.UserId;
    }

    private static bool ValidRate(decimal value)
    {
        return value >= 0 && value <= 1 && decimal.Round(value, 6) == value;
    }

    private static TaxFeePolicyDTO Map(TaxFeePolicy policy)
    {
        return new TaxFeePolicyDTO
        {
            TaxFeePolicyId = policy.TaxFeePolicyId,
            Version = policy.Version,
            VatRate = policy.VatRate,
            ServiceRate = policy.ServiceRate,
            ServiceEnabled = policy.ServiceEnabled,
            EffectiveFrom = new DateTimeOffset(DateTime.SpecifyKind(policy.EffectiveFrom, DateTimeKind.Utc))
        };
    }
}
