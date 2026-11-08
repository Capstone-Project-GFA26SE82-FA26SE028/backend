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
}

