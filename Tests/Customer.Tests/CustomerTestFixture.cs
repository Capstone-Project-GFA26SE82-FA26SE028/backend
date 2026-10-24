using Customer.BLL.Implements;
using Customer.BLL.Interfaces;
using Customer.DAL.Implements;
using Customer.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.UnitOfWorks;

namespace Customer.Tests;

public sealed class CustomerTestFixture : IDisposable
{
    public CustomerDbContext Db { get; }
    public ICustomerLoyaltyService LoyaltyService { get; }
    public IFeedbackService FeedbackService { get; }
    public TestTimeProvider Clock { get; }
    private readonly SqliteConnection _connection;

    public CustomerTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new CustomerDbContext(options);
        Db.Database.EnsureCreated();

        Clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

        SeedInitialData();

        var uow = new UnitOfWork<CustomerDbContext>(Db);
        var transaction = new CustomerTransaction(uow);
        var customerRepo = new CustomerRepository(Db);
        var loyaltyRepo = new LoyaltyRepository(Db);
        var feedbackRepo = new FeedbackRepository(Db);

        LoyaltyService = new CustomerLoyaltyService(customerRepo, loyaltyRepo, transaction, Clock);
        FeedbackService = new FeedbackService(feedbackRepo, customerRepo, transaction, Clock);
    }

    private void SeedInitialData()
    {
        var bronze = new LoyaltyTier
        {
            LoyaltyTierId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LoyaltyTierCode = "BRONZE",
            LoyaltyTierName = "Hạng Đồng",
            RequiredSpend = 0m,
            DiscountPercent = 0m
        };

        var silver = new LoyaltyTier
        {
            LoyaltyTierId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            LoyaltyTierCode = "SILVER",
            LoyaltyTierName = "Hạng Bạc",
            RequiredSpend = 1000000m,
            DiscountPercent = 5m
        };

        var gold = new LoyaltyTier
        {
            LoyaltyTierId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            LoyaltyTierCode = "GOLD",
            LoyaltyTierName = "Hạng Vàng",
            RequiredSpend = 5000000m,
            DiscountPercent = 10m
        };

        Db.LoyaltyTiers.AddRange(bronze, silver, gold);

        var policy = new LoyaltyPolicy
        {
            PolicyId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Version = 1,
            SpendPerPoint = 100000m, // 100k = 1 điểm
            MoneyPerRedeemedPoint = 1000m, // 1 điểm = 1k VNĐ
            EffectiveFrom = Clock.GetUtcNow().UtcDateTime.AddDays(-30)
        };

        Db.LoyaltyPolicies.Add(policy);
        Db.SaveChanges();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
