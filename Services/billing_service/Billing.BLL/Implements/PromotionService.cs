using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public class PromotionService : IPromotionService
{
    private readonly IPromotionRepository _promotions;
    private readonly IBillRepository _bills;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public PromotionService(
        IPromotionRepository promotions,
        IBillRepository bills,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _promotions = promotions;
        _bills = bills;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<PromotionDTO> Create(CreatePromotionDTO dto, CancellationToken ct)
    {
        ValidateCreate(dto);

        var promotion = new Promotion
        {
            PromotionId = Guid.NewGuid(),
            PromotionName = dto.PromotionName.Trim(),
            DiscountType = dto.DiscountType.Trim(),
            Type = dto.Type.Trim(),
            DiscountValue = dto.DiscountValue,
            UsageLimit = dto.UsageLimit,
            MinimumSpend = dto.MinimumSpend,
            StartAt = dto.StartAt,
            EndAt = dto.EndAt,
            IsActive = dto.IsActive
        };

        if (dto.Targets.Count > 0)
        {
            foreach (var target in dto.Targets)
            {
                promotion.PromotionTargets.Add(new PromotionTarget
                {
                    PromotionTargetId = Guid.NewGuid(),
                    PromotionId = promotion.PromotionId,
                    ItemRef = target.ItemRef,
                    CategoryRef = target.CategoryRef,
                    Role = string.IsNullOrWhiteSpace(target.Role) ? "Target" : target.Role.Trim(),
                    RequiredQuantity = target.RequiredQuantity <= 0 ? 1 : target.RequiredQuantity
                });
            }
        }

        await _promotions.AddAsync(promotion, ct);
        await _unitOfWork.SaveAsync(ct);

        return Map(promotion, 0);
    }

    public async Task<PromotionDTO> Update(Guid id, UpdatePromotionDTO dto, CancellationToken ct)
    {
        var promotion = await _promotions.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy chương trình khuyến mãi.");

        if (!string.IsNullOrWhiteSpace(dto.PromotionName))
        {
            promotion.PromotionName = dto.PromotionName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.DiscountType))
        {
            ValidateDiscountType(dto.DiscountType);
            promotion.DiscountType = dto.DiscountType.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.Type))
        {
            ValidateType(dto.Type);
            promotion.Type = dto.Type.Trim();
        }

        if (dto.DiscountValue.HasValue)
        {
            if (dto.DiscountValue.Value <= 0)
            {
                throw new BillingException(400, "Giá trị giảm giá phải lớn hơn 0.");
            }

            if (promotion.DiscountType == "Percentage" && dto.DiscountValue.Value > 100)
            {
                throw new BillingException(400, "Phần trăm giảm giá không được vượt quá 100%.");
            }

            promotion.DiscountValue = dto.DiscountValue.Value;
        }

        if (dto.UsageLimit.HasValue)
        {
            promotion.UsageLimit = dto.UsageLimit.Value;
        }

        if (dto.MinimumSpend.HasValue)
        {
            promotion.MinimumSpend = dto.MinimumSpend.Value;
        }

        if (dto.StartAt.HasValue)
        {
            promotion.StartAt = dto.StartAt.Value;
        }

        if (dto.EndAt.HasValue)
        {
            promotion.EndAt = dto.EndAt.Value;
        }

        if (dto.IsActive.HasValue)
        {
            promotion.IsActive = dto.IsActive.Value;
        }

        if (promotion.EndAt.HasValue && promotion.EndAt.Value < promotion.StartAt)
        {
            throw new BillingException(400, "Thời gian kết thúc không được sớm hơn thời gian bắt đầu.");
        }

        await _promotions.UpdateAsync(promotion, ct);
        await _unitOfWork.SaveAsync(ct);

        var usage = await _promotions.GetUsageCountAsync(id, ct);
        return Map(promotion, usage);
    }

    public async Task<PagedResult<PromotionDTO>> GetAll(PromotionQueryDTO query, CancellationToken ct)
    {
        var paged = await _promotions.GetAllAsync(query, ct);
        var dtos = new List<PromotionDTO>();

        foreach (var item in paged.Items)
        {
            var usage = await _promotions.GetUsageCountAsync(item.PromotionId, ct);
            dtos.Add(Map(item, usage));
        }

        return new PagedResult<PromotionDTO>(dtos, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<PromotionDTO> GetById(Guid id, CancellationToken ct)
    {
        var promotion = await _promotions.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy chương trình khuyến mãi.");

        var usage = await _promotions.GetUsageCountAsync(id, ct);
        return Map(promotion, usage);
    }

    public async Task<PromotionDTO> SetStatus(Guid id, bool isActive, CancellationToken ct)
    {
        var promotion = await _promotions.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy chương trình khuyến mãi.");

        promotion.IsActive = isActive;
        await _promotions.UpdateAsync(promotion, ct);
        await _unitOfWork.SaveAsync(ct);

        var usage = await _promotions.GetUsageCountAsync(id, ct);
        return Map(promotion, usage);
    }

    public async Task<List<ApplicablePromotionDTO>> GetAvailableForBill(Guid billId, CancellationToken ct)
    {
        var bill = await _bills.GetDetailsAsync(billId, ct)
            ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var activePromos = await _promotions.GetActivePromotionsAsync(now, ct);
        var result = new List<ApplicablePromotionDTO>();

        foreach (var promo in activePromos)
        {
            if (promo.UsageLimit.HasValue)
            {
                var usage = await _promotions.GetUsageCountAsync(promo.PromotionId, ct);
                if (usage >= promo.UsageLimit.Value)
                {
                    continue;
                }
            }

            if (promo.MinimumSpend.HasValue && bill.SubTotal < promo.MinimumSpend.Value)
            {
                continue;
            }

            decimal estimated = 0;
            if (promo.Type == "Order")
            {
                if (promo.DiscountType == "Percentage")
                {
                    estimated = decimal.Round(bill.SubTotal * (promo.DiscountValue / 100m), 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    estimated = Math.Min(bill.SubTotal, promo.DiscountValue);
                }
            }
            else if (promo.Type == "Item")
            {
                var targetItemIds = promo.PromotionTargets
                    .Where(t => t.ItemRef.HasValue)
                    .Select(t => t.ItemRef!.Value)
                    .ToHashSet();

                var matchingLinesTotal = bill.BillLines
                    .Where(l => targetItemIds.Contains(l.OrderItemRef))
                    .Sum(l => l.Quantity * l.UnitPriceSnapshot);

                if (matchingLinesTotal > 0)
                {
                    if (promo.DiscountType == "Percentage")
                    {
                        estimated = decimal.Round(matchingLinesTotal * (promo.DiscountValue / 100m), 2, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        estimated = Math.Min(matchingLinesTotal, promo.DiscountValue);
                    }
                }
            }
            else
            {
                if (promo.DiscountType == "Percentage")
                {
                    estimated = decimal.Round(bill.SubTotal * (promo.DiscountValue / 100m), 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    estimated = Math.Min(bill.SubTotal, promo.DiscountValue);
                }
            }

            if (estimated > 0)
            {
                result.Add(new ApplicablePromotionDTO
                {
                    PromotionId = promo.PromotionId,
                    PromotionName = promo.PromotionName,
                    DiscountType = promo.DiscountType,
                    Type = promo.Type,
                    DiscountValue = promo.DiscountValue,
                    EstimatedDiscount = estimated,
                    Description = promo.DiscountType == "Percentage"
                        ? $"Giảm {promo.DiscountValue}%"
                        : $"Giảm {promo.DiscountValue:N0} đ"
                });
            }
        }

        return result;
    }

    private static void ValidateCreate(CreatePromotionDTO dto)
    {
        if (dto is null)
        {
            throw new BillingException(400, "Dữ liệu khuyến mãi không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(dto.PromotionName))
        {
            throw new BillingException(400, "Tên chương trình khuyến mãi không được để trống.");
        }

        ValidateDiscountType(dto.DiscountType);
        ValidateType(dto.Type);

        if (dto.DiscountValue <= 0)
        {
            throw new BillingException(400, "Giá trị giảm giá phải lớn hơn 0.");
        }

        if (dto.DiscountType.Trim().Equals("Percentage", StringComparison.OrdinalIgnoreCase) && dto.DiscountValue > 100)
        {
            throw new BillingException(400, "Phần trăm giảm giá không được vượt quá 100%.");
        }

        if (dto.EndAt.HasValue && dto.EndAt.Value < dto.StartAt)
        {
            throw new BillingException(400, "Thời gian kết thúc không được sớm hơn thời gian bắt đầu.");
        }

        if (dto.MinimumSpend.HasValue && dto.MinimumSpend.Value < 0)
        {
            throw new BillingException(400, "Giá trị đơn tối thiểu không được âm.");
        }

        if (dto.UsageLimit.HasValue && dto.UsageLimit.Value < 1)
        {
            throw new BillingException(400, "Giới hạn sử dụng phải từ 1 trở lên.");
        }
    }

    private static void ValidateDiscountType(string discountType)
    {
        var normalized = discountType?.Trim();
        if (normalized != "Percentage" && normalized != "FixedAmount")
        {
            throw new BillingException(400, "Loại giảm giá phải là 'Percentage' hoặc 'FixedAmount'.");
        }
    }

    private static void ValidateType(string type)
    {
        var normalized = type?.Trim();
        if (normalized != "Order" && normalized != "Item" && normalized != "Category")
        {
            throw new BillingException(400, "Phạm vi áp dụng phải là 'Order', 'Item' hoặc 'Category'.");
        }
    }

    private static PromotionDTO Map(Promotion promotion, int currentUsage)
    {
        return new PromotionDTO
        {
            PromotionId = promotion.PromotionId,
            PromotionName = promotion.PromotionName,
            DiscountType = promotion.DiscountType,
            Type = promotion.Type,
            DiscountValue = promotion.DiscountValue,
            UsageLimit = promotion.UsageLimit,
            CurrentUsage = currentUsage,
            MinimumSpend = promotion.MinimumSpend,
            StartAt = promotion.StartAt,
            EndAt = promotion.EndAt,
            IsActive = promotion.IsActive,
            Targets = promotion.PromotionTargets.Select(t => new PromotionTargetDTO
            {
                PromotionTargetId = t.PromotionTargetId,
                PromotionId = t.PromotionId,
                ItemRef = t.ItemRef,
                CategoryRef = t.CategoryRef,
                Role = t.Role,
                RequiredQuantity = t.RequiredQuantity
            }).ToList()
        };
    }
}
