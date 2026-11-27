using System.Text.RegularExpressions;
using Customer.BLL.Interfaces;
using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Customer.DAL.Interfaces;
using Customer.DAL.Models;

namespace Customer.BLL.Implements;

public sealed class CustomerLoyaltyService(
    ICustomerRepository customers,
    ILoyaltyRepository loyalty,
    ICustomerTransaction transaction,
    TimeProvider clock,
    ILoyaltyManagerAccess managerAccess) : ICustomerLoyaltyService
{
    public async Task<CustomerDTO?> GetById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new CustomerException(400, "Mã khách hàng không hợp lệ.");
        }

        var customer = await customers.GetByIdAsync(id, ct);
        if (customer is null)
        {
            return null;
        }

        return await MapCustomerDTO(customer, ct);
    }

    public async Task<CustomerDTO?> GetByPhone(string phone, CancellationToken ct)
    {
        var normalized = NormalizePhoneNumber(phone);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CustomerException(400, "Số điện thoại là bắt buộc.");
        }

        var customer = await customers.GetByPhoneAsync(normalized, ct);
        if (customer is null)
        {
            return null;
        }

        return await MapCustomerDTO(customer, ct);
    }

    public async Task<CustomerDTO> GetOrCreate(CreateCustomerDTO request, CancellationToken ct)
    {
        if (request is null)
        {
            throw new CustomerException(400, "Thông tin khách hàng là bắt buộc.");
        }

        var normalized = NormalizePhoneNumber(request.PhoneNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CustomerException(400, "Số điện thoại không hợp lệ.");
        }

        var existing = await customers.GetByPhoneAsync(normalized, ct);
        if (existing is not null)
        {
            return await MapCustomerDTO(existing, ct);
        }

        var name = string.IsNullOrWhiteSpace(request.CustomerName)
            ? $"Khách {normalized}"
            : request.CustomerName.Trim();

        var defaultTier = await loyalty.GetDefaultTierAsync(ct);
        if (defaultTier is null)
        {
            throw new CustomerException(500, "Chưa có hạng thành viên nào được cấu hình.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            // Double check inside transaction
            var checkAgain = await customers.GetByPhoneAsync(normalized, ct);
            if (checkAgain is not null)
            {
                return await MapCustomerDTO(checkAgain, ct);
            }

            var newCustomer = new Customer.DAL.Models.Customer
            {
                CustomerId = Guid.NewGuid(),
                NormalizedPhone = normalized,
                CustomerName = name,
                LoyaltyTierId = defaultTier.LoyaltyTierId
            };

            customers.Create(newCustomer);
            return await MapCustomerDTO(newCustomer, ct, defaultTier);
        }, ct);
    }

    public async Task<(List<CustomerDTO> Items, int Total)> GetAll(
        int page,
        int pageSize,
        string? keyword,
        CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            throw new CustomerException(400, "Phân trang không hợp lệ.");
        }

        var (items, total) = await customers.GetPageAsync(page, pageSize, keyword, ct);
        var dtos = new List<CustomerDTO>(items.Count);

        foreach (var c in items)
        {
            dtos.Add(await MapCustomerDTO(c, ct));
        }

        return (dtos, total);
    }

    public async Task<List<LoyaltyTierDTO>> GetTiers(CancellationToken ct)
    {
        var tiers = await loyalty.GetAllTiersAsync(ct);
        return tiers.Select(MapTierDTO).ToList();
    }

    public async Task<List<LoyaltyTierDTO>> GetTiersByRestaurant(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);
        var tiers = await loyalty.GetAllTiersAsync(ct);
        return tiers.Select(MapTierDTO).ToList();
    }

    public async Task<LoyaltyTierDTO> CreateTier(Guid restaurantId, SaveLoyaltyTierDTO request, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        ValidateTierRequest(request);
        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);

        var code = request.LoyaltyTierCode.Trim().ToUpperInvariant();
        var existing = await loyalty.GetTierByCodeAsync(code, ct);
        if (existing is not null)
        {
            throw new CustomerException(409, $"Mã hạng '{code}' đã tồn tại trong nhà hàng.");
        }

        var tier = new LoyaltyTier
        {
            LoyaltyTierId = Guid.NewGuid(),
            LoyaltyTierCode = code,
            LoyaltyTierName = request.LoyaltyTierName.Trim(),
            RequiredSpend = request.RequiredSpend,
            DiscountPercent = request.DiscountPercent,
            RestaurantId = restaurantId
        };

        return await transaction.ExecuteAsync(async () =>
        {
            loyalty.CreateTier(tier);
            return MapTierDTO(tier);
        }, ct);
    }

    public async Task<LoyaltyTierDTO> UpdateTier(Guid restaurantId, Guid tierId, SaveLoyaltyTierDTO request, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        if (tierId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã hạng thành viên không hợp lệ.");
        }

        ValidateTierRequest(request);
        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);

        var tier = await loyalty.GetTierByIdAsync(tierId, ct);
        if (tier is null)
        {
            throw new CustomerException(404, "Không tìm thấy hạng thành viên.");
        }

        var code = request.LoyaltyTierCode.Trim().ToUpperInvariant();
        var duplicate = await loyalty.GetTierByCodeAsync(code, ct);
        if (duplicate is not null && duplicate.LoyaltyTierId != tierId)
        {
            throw new CustomerException(409, $"Mã hạng '{code}' đã được sử dụng cho hạng khác.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            tier.LoyaltyTierCode = code;
            tier.LoyaltyTierName = request.LoyaltyTierName.Trim();
            tier.RequiredSpend = request.RequiredSpend;
            tier.DiscountPercent = request.DiscountPercent;
            return MapTierDTO(tier);
        }, ct);
    }

    public async Task DeleteTier(Guid restaurantId, Guid tierId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        if (tierId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã hạng thành viên không hợp lệ.");
        }

        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);

        var tier = await loyalty.GetTierByIdAsync(tierId, ct);
        if (tier is null)
        {
            throw new CustomerException(404, "Không tìm thấy hạng thành viên.");
        }

        var totalTiers = await loyalty.CountTiersAsync(ct);
        if (totalTiers <= 1)
        {
            throw new CustomerException(400, "Không thể xóa hạng thành viên duy nhất của nhà hàng.");
        }

        var hasCustomers = await loyalty.HasAssignedCustomersAsync(tierId, ct);
        if (hasCustomers)
        {
            throw new CustomerException(400, "Không thể xóa hạng thành viên đang có khách hàng sử dụng.");
        }

        await transaction.ExecuteAsync(async () =>
        {
            loyalty.DeleteTier(tier);
            return true;
        }, ct);
    }

    public async Task<LoyaltyPolicyDTO> GetPolicyByRestaurant(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);
        var policy = await loyalty.GetCurrentPolicyAsync(ct);
        if (policy is null)
        {
            return new LoyaltyPolicyDTO
            {
                PolicyId = Guid.Empty,
                RestaurantId = restaurantId,
                Version = 1,
                SpendPerPoint = 1000m,
                MoneyPerRedeemedPoint = 100m,
                EffectiveFrom = clock.GetUtcNow().UtcDateTime
            };
        }

        return MapPolicyDTO(policy);
    }

    public async Task<LoyaltyPolicyDTO> SavePolicy(Guid restaurantId, SaveLoyaltyPolicyDTO request, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã nhà hàng không hợp lệ.");
        }

        if (request is null)
        {
            throw new CustomerException(400, "Dữ liệu chính sách tích điểm không được để trống.");
        }

        if (request.SpendPerPoint <= 0)
        {
            throw new CustomerException(400, "Số tiền để tích 1 điểm phải lớn hơn 0.");
        }

        if (request.MoneyPerRedeemedPoint <= 0)
        {
            throw new CustomerException(400, "Số tiền quy đổi cho 1 điểm tiêu phải lớn hơn 0.");
        }

        await managerAccess.EnsureManagerAccessAsync(restaurantId, ct);

        var current = await loyalty.GetCurrentPolicyAsync(ct);
        var nextVersion = (current?.Version ?? 0) + 1;
        var now = clock.GetUtcNow().UtcDateTime;

        var newPolicy = new LoyaltyPolicy
        {
            PolicyId = Guid.NewGuid(),
            Version = nextVersion,
            SpendPerPoint = request.SpendPerPoint,
            MoneyPerRedeemedPoint = request.MoneyPerRedeemedPoint,
            EffectiveFrom = now,
            RestaurantId = restaurantId
        };

        return await transaction.ExecuteAsync(async () =>
        {
            loyalty.CreatePolicy(newPolicy);
            return MapPolicyDTO(newPolicy);
        }, ct);
    }

    private static void ValidateTierRequest(SaveLoyaltyTierDTO request)
    {
        if (request is null)
        {
            throw new CustomerException(400, "Thông tin hạng thành viên không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.LoyaltyTierCode))
        {
            throw new CustomerException(400, "Mã hạng thành viên là bắt buộc.");
        }

        if (request.LoyaltyTierCode.Trim().Length > 50)
        {
            throw new CustomerException(400, "Mã hạng thành viên tối đa 50 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(request.LoyaltyTierName))
        {
            throw new CustomerException(400, "Tên hạng thành viên là bắt buộc.");
        }

        if (request.LoyaltyTierName.Trim().Length > 100)
        {
            throw new CustomerException(400, "Tên hạng thành viên tối đa 100 ký tự.");
        }

        if (request.RequiredSpend < 0)
        {
            throw new CustomerException(400, "Mức chi tiêu yêu cầu không được âm.");
        }

        if (request.DiscountPercent < 0 || request.DiscountPercent > 100)
        {
            throw new CustomerException(400, "Tỷ lệ giảm giá phải từ 0% đến 100%.");
        }
    }

    private static LoyaltyTierDTO MapTierDTO(LoyaltyTier t) => new()
    {
        LoyaltyTierId = t.LoyaltyTierId,
        LoyaltyTierCode = t.LoyaltyTierCode,
        LoyaltyTierName = t.LoyaltyTierName,
        RequiredSpend = t.RequiredSpend,
        DiscountPercent = t.DiscountPercent
    };

    private static LoyaltyPolicyDTO MapPolicyDTO(LoyaltyPolicy p) => new()
    {
        PolicyId = p.PolicyId,
        RestaurantId = p.RestaurantId,
        Version = p.Version,
        SpendPerPoint = p.SpendPerPoint,
        MoneyPerRedeemedPoint = p.MoneyPerRedeemedPoint,
        EffectiveFrom = p.EffectiveFrom
    };

    public async Task<LoyaltyEarnResultDTO> EarnPoints(LoyaltyEarnRequestDTO request, CancellationToken ct)
    {
        if (request is null || request.CustomerId == Guid.Empty || request.BillId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã khách hàng và mã hóa đơn là bắt buộc.");
        }

        if (request.FinalAmount <= 0)
        {
            return new LoyaltyEarnResultDTO
            {
                PointsEarned = 0,
                NewTotalPoints = await loyalty.GetAvailablePointsAsync(request.CustomerId, ct),
                TierUpgraded = false
            };
        }

        var policy = await loyalty.GetCurrentPolicyAsync(ct);
        var spendPerPoint = policy?.SpendPerPoint ?? 100000m;
        var pointsToEarn = (int)Math.Floor(request.FinalAmount / spendPerPoint);

        return await transaction.ExecuteAsync(async () =>
        {
            var customer = await customers.GetByIdAsync(request.CustomerId, ct);
            if (customer is null)
            {
                throw new CustomerException(404, "Không tìm thấy khách hàng.");
            }

            var now = clock.GetUtcNow().UtcDateTime;

            if (pointsToEarn > 0)
            {
                loyalty.CreateEntry(new LoyaltyEntry
                {
                    LoyaltyEntryId = Guid.NewGuid(),
                    CustomerId = customer.CustomerId,
                    BillRef = request.BillId,
                    PolicyId = policy?.PolicyId ?? Guid.Empty,
                    Type = "Earn",
                    SignedPoint = pointsToEarn,
                    EligibleSpend = request.FinalAmount,
                    SourceEventId = $"BILL-{request.BillId:N}",
                    PostedAt = now
                });
            }

            // Check Tier upgrade based on total spend
            var totalSpent = await loyalty.GetTotalSpentAsync(customer.CustomerId, ct) + request.FinalAmount;
            var targetTier = await loyalty.GetTierForSpendAsync(totalSpent, ct);
            var upgraded = false;

            if (targetTier is not null && targetTier.LoyaltyTierId != customer.LoyaltyTierId)
            {
                customer.LoyaltyTierId = targetTier.LoyaltyTierId;
                customer.LoyaltyTier = targetTier;
                upgraded = true;
            }

            var availablePoints = await loyalty.GetAvailablePointsAsync(customer.CustomerId, ct);

            return new LoyaltyEarnResultDTO
            {
                PointsEarned = pointsToEarn,
                NewTotalPoints = availablePoints + pointsToEarn,
                LoyaltyTierName = targetTier?.LoyaltyTierName ?? customer.LoyaltyTier?.LoyaltyTierName ?? "Thành viên",
                TierUpgraded = upgraded
            };
        }, ct);
    }

    public async Task<PointReservationResultDTO> ReservePoints(PointReservationRequestDTO request, CancellationToken ct)
    {
        if (request is null || request.CustomerId == Guid.Empty || request.BillId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã khách hàng và mã hóa đơn là bắt buộc.");
        }

        if (request.Points <= 0)
        {
            throw new CustomerException(400, "Số điểm tích lũy cần giữ phải lớn hơn 0.");
        }

        var available = await loyalty.GetAvailablePointsAsync(request.CustomerId, ct);
        if (available < request.Points)
        {
            throw new CustomerException(400, $"Không đủ điểm tích lũy. Điểm hiện có: {available}, điểm yêu cầu: {request.Points}.");
        }

        var policy = await loyalty.GetCurrentPolicyAsync(ct);
        var moneyPerPoint = policy?.MoneyPerRedeemedPoint ?? 1000m;
        var discount = request.Points * moneyPerPoint;

        return await transaction.ExecuteAsync(async () =>
        {
            // Cancel existing reservation for same bill if any
            var existing = await loyalty.GetReservationByBillAsync(request.BillId, ct);
            if (existing is not null)
            {
                existing.Status = "Cancelled";
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var reservation = new PointReservation
            {
                PointReservationId = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                BillRef = request.BillId,
                Point = request.Points,
                Status = "Pending",
                ExpiredAt = now.AddMinutes(30)
            };

            loyalty.CreateReservation(reservation);

            return new PointReservationResultDTO
            {
                PointReservationId = reservation.PointReservationId,
                ReservedPoints = request.Points,
                DiscountAmount = discount,
                RemainingPoints = available - request.Points
            };
        }, ct);
    }

    public async Task CommitPoints(Guid billId, CancellationToken ct)
    {
        if (billId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã hóa đơn không hợp lệ.");
        }

        await transaction.ExecuteAsync(async () =>
        {
            var reservation = await loyalty.GetReservationByBillAsync(billId, ct);
            if (reservation is null || reservation.Status != "Pending")
            {
                return true;
            }

            reservation.Status = "Committed";
            var now = clock.GetUtcNow().UtcDateTime;
            var policy = await loyalty.GetCurrentPolicyAsync(ct);

            loyalty.CreateEntry(new LoyaltyEntry
            {
                LoyaltyEntryId = Guid.NewGuid(),
                CustomerId = reservation.CustomerId,
                BillRef = billId,
                PolicyId = policy?.PolicyId ?? Guid.Empty,
                Type = "Redeem",
                SignedPoint = -reservation.Point,
                EligibleSpend = 0m,
                SourceEventId = $"REDEEM-{billId:N}",
                PostedAt = now
            });

            return true;
        }, ct);
    }

    public async Task CancelPoints(Guid billId, CancellationToken ct)
    {
        if (billId == Guid.Empty)
        {
            throw new CustomerException(400, "Mã hóa đơn không hợp lệ.");
        }

        await transaction.ExecuteAsync(async () =>
        {
            var reservation = await loyalty.GetReservationByBillAsync(billId, ct);
            if (reservation is not null && reservation.Status == "Pending")
            {
                reservation.Status = "Cancelled";
            }

            return true;
        }, ct);
    }

    private static string NormalizePhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = Regex.Replace(phone, @"[^\d]", string.Empty);
        if (digits.StartsWith("84") && digits.Length > 2)
        {
            digits = "0" + digits.Substring(2);
        }

        return digits;
    }

    private async Task<CustomerDTO> MapCustomerDTO(
        Customer.DAL.Models.Customer customer,
        CancellationToken ct,
        LoyaltyTier? tier = null)
    {
        var resolvedTier = tier ?? customer.LoyaltyTier ?? await loyalty.GetTierByIdAsync(customer.LoyaltyTierId, ct);
        var points = await loyalty.GetAvailablePointsAsync(customer.CustomerId, ct);
        var totalSpent = await loyalty.GetTotalSpentAsync(customer.CustomerId, ct);

        return new CustomerDTO
        {
            CustomerId = customer.CustomerId,
            NormalizedPhone = customer.NormalizedPhone,
            CustomerName = customer.CustomerName,
            LoyaltyTierId = customer.LoyaltyTierId,
            LoyaltyTierName = resolvedTier?.LoyaltyTierName ?? "Thành viên",
            LoyaltyTierCode = resolvedTier?.LoyaltyTierCode ?? "BRONZE",
            DiscountPercent = resolvedTier?.DiscountPercent ?? 0m,
            AvailablePoints = points,
            TotalSpent = totalSpent
        };
    }
}

