using System.Text.RegularExpressions;
using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashShiftSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "CodexBillingShift_";
    private readonly string _database = Prefix + Guid.NewGuid().ToString("N");
    private readonly Guid _restaurant = Guid.NewGuid();
    private readonly Guid _register = Guid.NewGuid();
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

        await using var db = Context();
        db.CashRegisters.Add(new CashRegister
        {
            CashRegisterId = _register,
            RestaurantId = _restaurant,
            Code = "SQL-SHIFT"
        });
        await db.SaveChangesAsync();
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
    public async Task ConcurrentCashiersWaitingOnTheRegisterProduceOneShiftAndOneConflict()
    {
        await using var locker = new SqlConnection(_connection);
        await locker.OpenAsync();
        await using var transaction = (SqlTransaction)await locker.BeginTransactionAsync();
        await using var command = new SqlCommand(
            "SELECT CashRegisterId FROM dbo.CashRegister WITH (UPDLOCK, HOLDLOCK) WHERE CashRegisterId = @id;", locker, transaction);
        command.Parameters.AddWithValue("@id", _register);
        await command.ExecuteScalarAsync();
        var firstCashier = Guid.NewGuid();
        var secondCashier = Guid.NewGuid();
        var first = Open(_register, firstCashier, 100);
        var second = Open(_register, secondCashier, 200);
        await Task.Delay(150);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await transaction.CommitAsync();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(new[] { 201, 409 }, results.Order().ToArray());
        await using var check = Context();
        var stored = await check.CashShifts.SingleAsync();
        Assert.Contains(stored.CashierUserRef, new[] { firstCashier, secondCashier });
        Assert.Equal(stored.CashierUserRef == firstCashier ? 100 : 200, stored.OpeningCash);
        Assert.Equal("Active", stored.Status);
        Assert.Null(stored.ClosedAt);
    }

    [BillingSqlServerFact]
    public async Task DifferentRegistersCanOpenWithoutInventingOneRegisterPerCashierRestriction()
    {
        var secondRegister = Guid.NewGuid();
        await using (var db = Context())
        {
            db.CashRegisters.Add(new CashRegister
            {
                CashRegisterId = secondRegister,
                RestaurantId = _restaurant,
                Code = "SQL-SHIFT-2"
            });
            await db.SaveChangesAsync();
        }

        var cashier = Guid.NewGuid();
        var results = await Task.WhenAll(Open(_register, cashier, 0), Open(secondRegister, cashier, 0));
        Assert.All(results, status => Assert.Equal(201, status));
        await using var check = Context();
        Assert.Equal(2, await check.CashShifts.CountAsync());
    }

    private async Task<int> Open(Guid register, Guid cashier, decimal amount)
    {
        await using var db = Context();
        var identity = new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = cashier,
                RoleId = "cashier",
                RestaurantIds = [_restaurant]
            }
        };
        var service = new CashShiftService(new CashRegisterRepository(db), new CashShiftRepository(db),
            new UnitOfWork<BillingDbContext>(db), identity, Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"]
            }), TimeProvider.System, new CashRegisterService(new CashRegisterRepository(db), new UnitOfWork<BillingDbContext>(db),
                identity, Options.Create(new CashRegisterAccessSettings
                {
                    CashierRoleIds = ["cashier"],
                    ManagerRoleIds = ["manager"]
                })));
        try
        {
            await service.Open(register, new OpenCashShiftDTO
            {
                OpeningCash = amount
            }, default);
            return 201;
        }
        catch (BillingException error)
        {
            return error.StatusCode;
        }
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
