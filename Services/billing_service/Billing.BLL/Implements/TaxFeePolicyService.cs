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
            throw new BillingException(400, "Provide VAT and service rates between 0 and 1 with at most six decimal places, and an explicit serviceEnabled value.");
        }

        DateTime? requestedUtc = null;
        if (request.EffectiveFrom is not null)
        {
            if (!DateTimeOffset.TryParseExact(request.EffectiveFrom, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp)
                || timestamp.UtcDateTime < SqlDateTime.MinValue.Value || timestamp.UtcDateTime > SqlDateTime.MaxValue.Value)
            {
                throw new BillingException(400, "Use an ISO 8601 effectiveFrom with an explicit UTC offset within the SQL datetime range, or omit it for immediate effect.");
            }

            requestedUtc = timestamp.UtcDateTime;
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await policies.LockCreateAsync(ct))
            {
                throw new BillingException(409, "Tax policy creation is busy. Retry the request.");
            }

            var currentActor = await RequireAdministrator(ct);
            if (currentActor != actor)
            {
                throw new BillingException(401, "The employee session changed. Sign in again.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var effective = DateTime.SpecifyKind(new SqlDateTime(requestedUtc ?? now).Value, DateTimeKind.Utc);
            if (requestedUtc.HasValue)
            {
                if (requestedUtc.Value <= now || effective <= now)
                {
                    throw new BillingException(400, "An explicit effectiveFrom must be in the future at SQL datetime precision. Omit it for immediate effect.");
                }
            }
            else if (effective > now)
            {
                effective = DateTime.SpecifyKind(new SqlDateTime(effective.AddMilliseconds(-3)).Value, DateTimeKind.Utc);
            }

            if (await policies.EffectiveFromExistsAsync(effective, ct))
            {
                throw new BillingException(409, "A tax policy already exists at this effective time, including SQL datetime rounding.");
            }

            var highestVersion = await policies.GetHighestVersionAsync(ct);
            if (highestVersion.HasValue && (highestVersion.Value < 1 || highestVersion.Value == int.MaxValue))
            {
                throw new BillingException(409, "Existing tax policy versions must be reviewed before creating another version.");
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
            throw new BillingException(400, "Provide valid pagination with a page size between 1 and 100.");
        }

        var result = await policies.GetPageAsync(query, ct);
        return new PagedResult<TaxFeePolicyDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<TaxFeePolicyDTO> GetById(Guid id, CancellationToken ct)
    {
        await RequireAdministrator(ct);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid tax policy ID is required.");
        }

        var policy = await policies.GetByIdAsync(id, ct) ?? throw new BillingException(404, "Tax policy not found.");
        return Map(policy);
    }

    private async Task<Guid> RequireAdministrator(CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        if (!actor.IsAdministrator)
        {
            throw new BillingException(403, "Only a System Administrator can manage tax and service fee policies.");
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
