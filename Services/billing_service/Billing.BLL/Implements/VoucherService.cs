using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public class VoucherService : IVoucherService
{
    private readonly IVoucherRepository _vouchers;
    private readonly IVoucherRedemptionRepository _redemptions;
    private readonly IBillAdjustmentRepository _adjustments;
    private readonly IBillRepository _bills;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public VoucherService(
        IVoucherRepository vouchers,
        IVoucherRedemptionRepository redemptions,
        IBillAdjustmentRepository adjustments,
        IBillRepository bills,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _vouchers = vouchers;
        _redemptions = redemptions;
        _adjustments = adjustments;
        _bills = bills;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<VoucherDTO> Create(CreateVoucherDTO dto, CancellationToken ct)
    {
        ValidateCreate(dto);

        var normalizedCode = dto.VoucherCode.Trim().ToUpperInvariant();
        var existing = await _vouchers.FindByCodeAsync(normalizedCode, ct);
        if (existing is not null)
        {
            throw new BillingException(409, $"Mã voucher '{normalizedCode}' đã tồn tại.");
        }

        var voucher = new Voucher
        {
            VoucherId = Guid.NewGuid(),
            NormalizedVoucherCode = normalizedCode,
            DiscountType = dto.DiscountType.Trim(),
            DiscountValue = dto.DiscountValue,
            MaximumDiscount = dto.MaximumDiscount,
            UsageLimit = dto.UsageLimit,
            StartAt = dto.StartAt,
            EndAt = dto.EndAt,
            IsActive = dto.IsActive
        };

        await _vouchers.AddAsync(voucher, ct);
        await _unitOfWork.SaveAsync(ct);

        return Map(voucher, 0);
    }

    public async Task<VoucherDTO> Update(Guid id, UpdateVoucherDTO dto, CancellationToken ct)
    {
        var voucher = await _vouchers.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy mã giảm giá.");

        if (!string.IsNullOrWhiteSpace(dto.DiscountType))
        {
            ValidateDiscountType(dto.DiscountType);
            voucher.DiscountType = dto.DiscountType.Trim();
        }

        if (dto.DiscountValue.HasValue)
        {
            if (dto.DiscountValue.Value <= 0)
            {
                throw new BillingException(400, "Giá trị giảm giá phải lớn hơn 0.");
            }

            if (voucher.DiscountType == "Percentage" && dto.DiscountValue.Value > 100)
            {
                throw new BillingException(400, "Phần trăm giảm giá không được vượt quá 100%.");
            }

            voucher.DiscountValue = dto.DiscountValue.Value;
        }

        if (dto.MaximumDiscount.HasValue)
        {
            voucher.MaximumDiscount = dto.MaximumDiscount.Value;
        }

        if (dto.UsageLimit.HasValue)
        {
            voucher.UsageLimit = dto.UsageLimit.Value;
        }

        if (dto.StartAt.HasValue)
        {
            voucher.StartAt = dto.StartAt.Value;
        }

        if (dto.EndAt.HasValue)
        {
            voucher.EndAt = dto.EndAt.Value;
        }

        if (dto.IsActive.HasValue)
        {
            voucher.IsActive = dto.IsActive.Value;
        }

        if (voucher.EndAt.HasValue && voucher.EndAt.Value < voucher.StartAt)
        {
            throw new BillingException(400, "Thời gian kết thúc không được sớm hơn thời gian bắt đầu.");
        }

        await _vouchers.UpdateAsync(voucher, ct);
        await _unitOfWork.SaveAsync(ct);

        var usage = await _vouchers.GetUsageCountAsync(id, ct);
        return Map(voucher, usage);
    }

    public async Task<PagedResult<VoucherDTO>> GetAll(VoucherQueryDTO query, CancellationToken ct)
    {
        var paged = await _vouchers.GetAllAsync(query, ct);
        var dtos = new List<VoucherDTO>();

        foreach (var item in paged.Items)
        {
            var usage = await _vouchers.GetUsageCountAsync(item.VoucherId, ct);
            dtos.Add(Map(item, usage));
        }

        return new PagedResult<VoucherDTO>(dtos, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<VoucherDTO> GetById(Guid id, CancellationToken ct)
    {
        var voucher = await _vouchers.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy mã giảm giá.");

        var usage = await _vouchers.GetUsageCountAsync(id, ct);
        return Map(voucher, usage);
    }

    public async Task<VoucherDTO> SetStatus(Guid id, bool isActive, CancellationToken ct)
    {
        var voucher = await _vouchers.GetByIdAsync(id, ct)
            ?? throw new BillingException(404, "Không tìm thấy mã giảm giá.");

        voucher.IsActive = isActive;
        await _vouchers.UpdateAsync(voucher, ct);
        await _unitOfWork.SaveAsync(ct);

        var usage = await _vouchers.GetUsageCountAsync(id, ct);
        return Map(voucher, usage);
    }

    public async Task<BillDTO> ApplyVoucher(Guid billId, string voucherCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(voucherCode))
        {
            throw new BillingException(400, "Vui lòng nhập mã voucher.");
        }

        var normalizedCode = voucherCode.Trim().ToUpperInvariant();
        var voucher = await _vouchers.FindByCodeAsync(normalizedCode, ct)
            ?? throw new BillingException(404, $"Không tìm thấy mã voucher '{normalizedCode}'.");

        var now = _clock.GetUtcNow().UtcDateTime;
        if (!voucher.IsActive)
        {
            throw new BillingException(400, "Mã voucher này hiện đang bị tạm khóa.");
        }

        if (voucher.StartAt > now)
        {
            throw new BillingException(400, "Mã voucher này chưa đến thời gian áp dụng.");
        }

        if (voucher.EndAt.HasValue && voucher.EndAt.Value < now)
        {
            throw new BillingException(400, "Mã voucher này đã hết hạn sử dụng.");
        }

        var bill = await _bills.GetDetailsAsync(billId, ct)
            ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");

        if (bill.Status != "Unpaid")
        {
            throw new BillingException(409, "Chỉ có thể áp dụng mã giảm giá cho hóa đơn chưa thanh toán.");
        }

        var existingRedemption = await _redemptions.GetByBillAndVoucherAsync(billId, voucher.VoucherId, ct);
        if (existingRedemption is not null && existingRedemption.Status != "Cancelled")
        {
            throw new BillingException(409, "Mã voucher này đã được áp dụng cho hóa đơn.");
        }

        var billRedemptions = await _redemptions.GetByBillIdAsync(billId, ct);
        if (billRedemptions.Any(r => r.Status != "Cancelled"))
        {
            throw new BillingException(409, "Hóa đơn đã được áp dụng mã giảm giá khác. Vui lòng hủy mã cũ trước khi áp dụng mã mới.");
        }

        if (voucher.UsageLimit.HasValue)
        {
            var usage = await _vouchers.GetUsageCountAsync(voucher.VoucherId, ct);
            if (usage >= voucher.UsageLimit.Value)
            {
                throw new BillingException(409, "Mã voucher này đã hết lượt sử dụng.");
            }
        }

        decimal discountAmount;
        if (voucher.DiscountType == "Percentage")
        {
            discountAmount = decimal.Round(bill.SubTotal * (voucher.DiscountValue / 100m), 2, MidpointRounding.AwayFromZero);
            if (voucher.MaximumDiscount.HasValue && discountAmount > voucher.MaximumDiscount.Value)
            {
                discountAmount = voucher.MaximumDiscount.Value;
            }
        }
        else
        {
            discountAmount = Math.Min(bill.SubTotal, voucher.DiscountValue);
        }

        if (discountAmount <= 0)
        {
            throw new BillingException(400, "Giá trị giảm giá của voucher không hợp lệ trên hóa đơn này.");
        }

        var snapshot = JsonSerializer.Serialize(new
        {
            Code = voucher.NormalizedVoucherCode,
            voucher.DiscountType,
            voucher.DiscountValue,
            voucher.MaximumDiscount
        });

        var adjustment = new BillAdjustment
        {
            BillAdjustmentId = Guid.NewGuid(),
            BillId = bill.BillId,
            VoucherId = voucher.VoucherId,
            Type = "Voucher",
            Amount = discountAmount,
            RuleSnapshot = snapshot,
            Reason = $"Áp dụng voucher {voucher.NormalizedVoucherCode}"
        };

        var redemption = new VoucherRedemption
        {
            VoucherRedemptionId = Guid.NewGuid(),
            VoucherId = voucher.VoucherId,
            BillId = bill.BillId,
            Status = "Reserved",
            ReservedUntil = now.AddHours(2)
        };

        await _adjustments.AddAsync(adjustment, ct);
        await _redemptions.AddAsync(redemption, ct);

        bill.DiscountAmount += discountAmount;
        RecalculateTotals(bill);

        await _unitOfWork.SaveAsync(ct);

        bill.BillAdjustments.Add(adjustment);
        return BillService.Map(bill);
    }

    public async Task<BillDTO> RemoveVoucher(Guid billId, Guid voucherId, CancellationToken ct)
    {
        var bill = await _bills.GetDetailsAsync(billId, ct)
            ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");

        if (bill.Status != "Unpaid")
        {
            throw new BillingException(409, "Chỉ có thể thay đổi giảm giá trên hóa đơn chưa thanh toán.");
        }

        var redemption = await _redemptions.GetByBillAndVoucherAsync(billId, voucherId, ct);
        if (redemption is null)
        {
            throw new BillingException(404, "Không tìm thấy mã giảm giá áp dụng trên hóa đơn này.");
        }

        var billAdjustments = await _adjustments.GetByBillIdAsync(billId, ct);
        var voucherAdjustment = billAdjustments.FirstOrDefault(a => a.VoucherId == voucherId);

        if (voucherAdjustment is not null)
        {
            bill.DiscountAmount = Math.Max(0, bill.DiscountAmount - voucherAdjustment.Amount);
            await _adjustments.RemoveByBillAndVoucherAsync(billId, voucherId, ct);
        }

        await _redemptions.DeleteAsync(redemption, ct);
        RecalculateTotals(bill);

        await _unitOfWork.SaveAsync(ct);

        var updatedAdjustments = await _adjustments.GetByBillIdAsync(billId, ct);
        bill.BillAdjustments = updatedAdjustments;
        return BillService.Map(bill);
    }

    private static void RecalculateTotals(Bill bill)
    {
        var discountedSubTotal = Math.Max(0, bill.SubTotal - bill.DiscountAmount);
        bill.ServiceAmount = decimal.Round(discountedSubTotal * bill.ServiceRateSnapshot, 2, MidpointRounding.AwayFromZero);
        bill.VatAmount = decimal.Round((discountedSubTotal + bill.ServiceAmount) * bill.VatRateSnapshot, 2, MidpointRounding.AwayFromZero);
        bill.TotalAmount = decimal.Round(discountedSubTotal + bill.ServiceAmount + bill.VatAmount, 0, MidpointRounding.AwayFromZero);
    }

    private static void ValidateCreate(CreateVoucherDTO dto)
    {
        if (dto is null)
        {
            throw new BillingException(400, "Dữ liệu voucher không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(dto.VoucherCode))
        {
            throw new BillingException(400, "Mã voucher không được để trống.");
        }

        ValidateDiscountType(dto.DiscountType);

        if (dto.DiscountValue <= 0)
        {
            throw new BillingException(400, "Giá trị giảm giá phải lớn hơn 0.");
        }

        if (dto.DiscountType.Trim().Equals("Percentage", StringComparison.OrdinalIgnoreCase) && dto.DiscountValue > 100)
        {
            throw new BillingException(400, "Phần trăm giảm giá không được vượt quá 100%.");
        }

        if (dto.MaximumDiscount.HasValue && dto.MaximumDiscount.Value <= 0)
        {
            throw new BillingException(400, "Mức giảm tối đa phải lớn hơn 0.");
        }

        if (dto.EndAt.HasValue && dto.EndAt.Value < dto.StartAt)
        {
            throw new BillingException(400, "Thời gian kết thúc không được sớm hơn thời gian bắt đầu.");
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

    private static VoucherDTO Map(Voucher voucher, int currentUsage)
    {
        return new VoucherDTO
        {
            VoucherId = voucher.VoucherId,
            NormalizedVoucherCode = voucher.NormalizedVoucherCode,
            DiscountType = voucher.DiscountType,
            DiscountValue = voucher.DiscountValue,
            MaximumDiscount = voucher.MaximumDiscount,
            UsageLimit = voucher.UsageLimit,
            CurrentUsage = currentUsage,
            StartAt = voucher.StartAt,
            EndAt = voucher.EndAt,
            IsActive = voucher.IsActive
        };
    }
}
