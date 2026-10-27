using System.Text.RegularExpressions;
using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class TaxFeePolicySqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingTax_";
    private readonly string _database = Prefix + Guid.NewGuid().ToString("N");
    private string _master = string.Empty;
    private string _connection = string.Empty;
    private bool _created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BILLING_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        RequireOwnedDatabase();
        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        _master = builder.ConnectionString;
        builder.InitialCatalog = _database;
        _connection = builder.ConnectionString;
        await Execute(_master, $"CREATE DATABASE [{_database}];");
        _created = true;
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingDb.sql"));
        foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await Execute(_connection, batch);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_created)
        {
            RequireOwnedDatabase();
            await Execute(_master, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];");
        }
    }

    [BillingSqlServerFact]
    public async Task ConcurrentPoliciesForDifferentInstantsHaveDistinctSequentialVersions()
    {
        var firstTime = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
        var secondTime = DateTimeOffset.UtcNow.AddHours(2).ToString("O");
        var result = await CreateConcurrently(firstTime, secondTime);
        Assert.Equal(new[] { 201, 201 }, result.Order().ToArray());
        await using var db = Context();
        var versions = await db.TaxFeePolicies.OrderBy(policy => policy.Version).Select(policy => policy.Version).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, versions);
        Assert.Equal(2, await db.TaxFeePolicies.Select(policy => policy.EffectiveFrom).Distinct().CountAsync());
    }

    [BillingSqlServerFact]
    public async Task ConcurrentPoliciesForOneInstantProduceOnePolicyAndOneConflict()
    {
        var instant = DateTimeOffset.UtcNow.AddHours(1);
        var result = await CreateConcurrently(instant.ToString("O"), instant.ToOffset(TimeSpan.FromHours(7)).ToString("O"));
        Assert.Equal(new[] { 201, 409 }, result.Order().ToArray());
        await using var db = Context();
        var policy = await db.TaxFeePolicies.SingleAsync();
        Assert.Equal(1, policy.Version);
        Assert.Equal(0.08m, policy.VatRate);
    }

    [BillingSqlServerFact]
    public async Task SqlPrecisionCollisionIsRejectedAndImmediatePolicyIsAlreadyEffective()
    {
        await using var db = Context();
        var service = Service(db);
        var future = DateTime.UtcNow.Date.AddDays(2).AddHours(12);
        var first = TaxFeePolicyServiceTests.Request();
        first.EffectiveFrom = future.AddTicks(1000).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        await service.Create(first, default);
        var second = TaxFeePolicyServiceTests.Request();
        second.EffectiveFrom = future.AddTicks(2000).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        Assert.Equal(409, (await Assert.ThrowsAsync<BillingException>(() => service.Create(second, default))).StatusCode);
        var immediate = await service.Create(TaxFeePolicyServiceTests.Request(), default);
        var effective = await new TaxFeePolicyRepository(db).GetEffectiveAsync(DateTime.UtcNow, default);
        Assert.Equal(immediate.TaxFeePolicyId, Assert.Single(effective).TaxFeePolicyId);
        Assert.Equal(2, await db.TaxFeePolicies.CountAsync());
        Assert.Empty(await db.Bills.ToListAsync());
    }

    private async Task<int[]> CreateConcurrently(string firstTime, string secondTime)
    {
        await using var locker = new SqlConnection(_connection);
        await locker.OpenAsync();
        await using var transaction = (SqlTransaction)await locker.BeginTransactionAsync();
        await using var command = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'Billing.TaxFeePolicy.Create', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;", locker, transaction);
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        var first = CreatePolicy(firstTime);
        var second = CreatePolicy(secondTime);
        await Task.Delay(150);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await transaction.CommitAsync();
        return await Task.WhenAll(first, second);
    }

    private async Task<int> CreatePolicy(string effectiveFrom)
    {
        await using var db = Context();
        var request = TaxFeePolicyServiceTests.Request();
        request.EffectiveFrom = effectiveFrom;
        try
        {
            await Service(db).Create(request, default);
            return 201;
        }
        catch (BillingException error)
        {
            return error.StatusCode;
        }
    }

    private static TaxFeePolicyService Service(BillingDbContext db)
    {
        var identity = new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = Guid.NewGuid(),
                RoleId = "admin",
                IsAdministrator = true
            }
        };
        return new TaxFeePolicyService(new TaxFeePolicyRepository(db), new UnitOfWork<BillingDbContext>(db), identity, TimeProvider.System);
    }

    private BillingDbContext Context()
    {
        return new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(_connection).Options);
    }

    private void RequireOwnedDatabase()
    {
        if (!_database.StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(_database[Prefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Only this test's isolated Billing database may be created or removed.");
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
