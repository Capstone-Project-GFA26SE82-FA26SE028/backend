using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Xunit;

namespace Customer.Tests;

public sealed class CustomerLoyaltyTests
{
    [Fact]
    public async Task GetOrCreate_NewPhone_CreatesCustomerWithDefaultBronzeTier()
    {
        using var fixture = new CustomerTestFixture();

        var result = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0987654321",
            CustomerName = "Nguyễn Văn A"
        }, default);

        Assert.NotNull(result);
        Assert.Equal("0987654321", result.NormalizedPhone);
        Assert.Equal("Nguyễn Văn A", result.CustomerName);
        Assert.Equal("BRONZE", result.LoyaltyTierCode);
        Assert.Equal(0, result.AvailablePoints);
    }

    [Fact]
    public async Task GetOrCreate_ExistingPhone_ReturnsExistingProfile()
    {
        using var fixture = new CustomerTestFixture();

        var first = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "+84 987 654 321",
            CustomerName = "Trần Thị B"
        }, default);

        var second = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0987654321",
            CustomerName = "Tên Khác"
        }, default);

        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal("Trần Thị B", second.CustomerName);
    }

    [Fact]
    public async Task EarnPoints_CalculatesCorrectPointsBasedOnPolicy()
    {
        using var fixture = new CustomerTestFixture();

        var customer = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0912345678",
            CustomerName = "Lê Văn C"
        }, default);

        var billId = Guid.NewGuid();
        // 550,000 VND / 100,000 = 5 points
        var earnResult = await fixture.LoyaltyService.EarnPoints(new LoyaltyEarnRequestDTO
        {
            CustomerId = customer.CustomerId,
            BillId = billId,
            FinalAmount = 550000m
        }, default);

        Assert.Equal(5, earnResult.PointsEarned);
        Assert.Equal(5, earnResult.NewTotalPoints);

        var profile = await fixture.LoyaltyService.GetById(customer.CustomerId, default);
        Assert.NotNull(profile);
        Assert.Equal(5, profile.AvailablePoints);
    }

    [Fact]
    public async Task EarnPoints_CumulativeSpend_AutoUpgradesTier()
    {
        using var fixture = new CustomerTestFixture();

        var customer = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0933333333",
            CustomerName = "Đại Gia D"
        }, default);

        Assert.Equal("BRONZE", customer.LoyaltyTierCode);

        // Spend 1,200,000 VND -> qualifies for SILVER (>= 1,000,000)
        var earnResult = await fixture.LoyaltyService.EarnPoints(new LoyaltyEarnRequestDTO
        {
            CustomerId = customer.CustomerId,
            BillId = Guid.NewGuid(),
            FinalAmount = 1200000m
        }, default);

        Assert.True(earnResult.TierUpgraded);
        Assert.Equal("Hạng Bạc", earnResult.LoyaltyTierName);

        var profile = await fixture.LoyaltyService.GetById(customer.CustomerId, default);
        Assert.NotNull(profile);
        Assert.Equal("SILVER", profile.LoyaltyTierCode);
        Assert.Equal(5m, profile.DiscountPercent);
    }

    [Fact]
    public async Task ReserveAndCommitPoints_ReducesAvailablePointsCorrectly()
    {
        using var fixture = new CustomerTestFixture();

        var customer = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0944444444",
            CustomerName = "Khách Tích Điểm"
        }, default);

        // Earn 20 points (2,000,000 spend)
        await fixture.LoyaltyService.EarnPoints(new LoyaltyEarnRequestDTO
        {
            CustomerId = customer.CustomerId,
            BillId = Guid.NewGuid(),
            FinalAmount = 2000000m
        }, default);

        var billId = Guid.NewGuid();
        // Reserve 10 points
        var reserveResult = await fixture.LoyaltyService.ReservePoints(new PointReservationRequestDTO
        {
            CustomerId = customer.CustomerId,
            BillId = billId,
            Points = 10
        }, default);

        Assert.Equal(10, reserveResult.ReservedPoints);
        Assert.Equal(10000m, reserveResult.DiscountAmount); // 10 points * 1000 = 10,000 VND
        Assert.Equal(10, reserveResult.RemainingPoints);

        // Commit points
        await fixture.LoyaltyService.CommitPoints(billId, default);

        var profile = await fixture.LoyaltyService.GetById(customer.CustomerId, default);
        Assert.NotNull(profile);
        Assert.Equal(10, profile.AvailablePoints);
    }

    [Fact]
    public async Task ReservePoints_InsufficientPoints_ThrowsCustomerException()
    {
        using var fixture = new CustomerTestFixture();

        var customer = await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0955555555",
            CustomerName = "Khách Hàng Mới"
        }, default);

        await Assert.ThrowsAsync<CustomerException>(() => fixture.LoyaltyService.ReservePoints(new PointReservationRequestDTO
        {
            CustomerId = customer.CustomerId,
            BillId = Guid.NewGuid(),
            Points = 50
        }, default));
    }

    [Fact]
    public async Task CreateTier_ValidRequest_CreatesSuccessfully()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        var created = await fixture.LoyaltyService.CreateTier(restaurantId, new SaveLoyaltyTierDTO
        {
            LoyaltyTierCode = "DIAMOND",
            LoyaltyTierName = "Hạng Kim Cương",
            RequiredSpend = 10000000m,
            DiscountPercent = 15m
        }, default);

        Assert.NotNull(created);
        Assert.Equal("DIAMOND", created.LoyaltyTierCode);
        Assert.Equal("Hạng Kim Cương", created.LoyaltyTierName);
        Assert.Equal(10000000m, created.RequiredSpend);
        Assert.Equal(15m, created.DiscountPercent);
    }

    [Fact]
    public async Task CreateTier_DuplicateCode_ThrowsCustomerException()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<CustomerException>(() => fixture.LoyaltyService.CreateTier(restaurantId, new SaveLoyaltyTierDTO
        {
            LoyaltyTierCode = "BRONZE",
            LoyaltyTierName = "Trùng Hạng Đồng",
            RequiredSpend = 0m,
            DiscountPercent = 0m
        }, default));

        Assert.Equal(409, ex.StatusCode);
    }

    [Theory]
    [InlineData("", "Tên", 0, 0)]
    [InlineData("CODE", "", 0, 0)]
    [InlineData("CODE", "Tên", -1, 0)]
    [InlineData("CODE", "Tên", 0, -5)]
    [InlineData("CODE", "Tên", 0, 105)]
    public async Task CreateTier_InvalidData_ThrowsCustomerException(string code, string name, decimal spend, decimal discount)
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<CustomerException>(() => fixture.LoyaltyService.CreateTier(restaurantId, new SaveLoyaltyTierDTO
        {
            LoyaltyTierCode = code,
            LoyaltyTierName = name,
            RequiredSpend = spend,
            DiscountPercent = discount
        }, default));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateTier_ValidRequest_UpdatesSuccessfully()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();
        var silverId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var updated = await fixture.LoyaltyService.UpdateTier(restaurantId, silverId, new SaveLoyaltyTierDTO
        {
            LoyaltyTierCode = "SILVER_PLUS",
            LoyaltyTierName = "Hạng Bạc Plus",
            RequiredSpend = 1500000m,
            DiscountPercent = 7m
        }, default);

        Assert.NotNull(updated);
        Assert.Equal("SILVER_PLUS", updated.LoyaltyTierCode);
        Assert.Equal("Hạng Bạc Plus", updated.LoyaltyTierName);
        Assert.Equal(1500000m, updated.RequiredSpend);
        Assert.Equal(7m, updated.DiscountPercent);
    }

    [Fact]
    public async Task DeleteTier_HasAssignedCustomers_ThrowsCustomerException()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        // Create a customer with default bronze tier
        await fixture.LoyaltyService.GetOrCreate(new CreateCustomerDTO
        {
            PhoneNumber = "0966666666",
            CustomerName = "Khách Hạng Đồng"
        }, default);

        var bronzeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var ex = await Assert.ThrowsAsync<CustomerException>(() =>
            fixture.LoyaltyService.DeleteTier(restaurantId, bronzeId, default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("khách hàng", ex.Message);
    }

    [Fact]
    public async Task DeleteTier_ValidRequest_DeletesSuccessfully()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();
        var goldId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await fixture.LoyaltyService.DeleteTier(restaurantId, goldId, default);

        var tiers = await fixture.LoyaltyService.GetTiers(default);
        Assert.DoesNotContain(tiers, t => t.LoyaltyTierId == goldId);
    }

    [Fact]
    public async Task ManagerAccess_DenyAccess_Throws403()
    {
        using var fixture = new CustomerTestFixture();
        fixture.ManagerAccess.DenyAccess = true;
        var restaurantId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<CustomerException>(() =>
            fixture.LoyaltyService.GetTiersByRestaurant(restaurantId, default));

        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task GetAndSavePolicy_ValidRequest_IncrementsVersion()
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        var current = await fixture.LoyaltyService.GetPolicyByRestaurant(restaurantId, default);
        Assert.NotNull(current);
        Assert.Equal(1, current.Version);

        var saved = await fixture.LoyaltyService.SavePolicy(restaurantId, new SaveLoyaltyPolicyDTO
        {
            SpendPerPoint = 50000m,
            MoneyPerRedeemedPoint = 2000m
        }, default);

        Assert.NotNull(saved);
        Assert.Equal(2, saved.Version);
        Assert.Equal(50000m, saved.SpendPerPoint);
        Assert.Equal(2000m, saved.MoneyPerRedeemedPoint);

        var refreshed = await fixture.LoyaltyService.GetPolicyByRestaurant(restaurantId, default);
        Assert.Equal(2, refreshed.Version);
        Assert.Equal(50000m, refreshed.SpendPerPoint);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(-100, 1000)]
    [InlineData(1000, 0)]
    [InlineData(1000, -50)]
    public async Task SavePolicy_InvalidValues_ThrowsCustomerException(decimal spend, decimal money)
    {
        using var fixture = new CustomerTestFixture();
        var restaurantId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<CustomerException>(() => fixture.LoyaltyService.SavePolicy(restaurantId, new SaveLoyaltyPolicyDTO
        {
            SpendPerPoint = spend,
            MoneyPerRedeemedPoint = money
        }, default));

        Assert.Equal(400, ex.StatusCode);
    }
}

