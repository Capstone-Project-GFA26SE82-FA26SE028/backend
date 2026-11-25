using System.Text.RegularExpressions;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class CashierBoardRepositorySqlTests : IAsyncLifetime
{
    private readonly string database = "CashierBoardTest_" + Guid.NewGuid().ToString("N");
    private string master = string.Empty;
    private string connection = string.Empty;
    private bool created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BILLING_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        master = builder.ConnectionString;
        builder.InitialCatalog = database;
        connection = builder.ConnectionString;
        await Execute(master, $"CREATE DATABASE [{database}];");
        created = true;
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingDb.sql"));
        foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await Execute(connection, batch);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (created && database.StartsWith("CashierBoardTest_", StringComparison.Ordinal)
            && Guid.TryParseExact(database["CashierBoardTest_".Length..], "N", out _))
        {
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
    }

    [BillingSqlServerFact]
    public async Task PaidCardsRequireSuccessfulPaymentInSelectedShiftAndDoNotDuplicateBillLines()
    {
        await using var db = Context();
        var restaurantId = Guid.NewGuid();
        var register = new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = "DEFAULT"
        };
        var current = Shift(register, closed: false);
        var previous = Shift(register, closed: true);
        db.CashShifts.AddRange(current, previous);
        db.PaymentMethods.Add(new PaymentMethod
        {
            MethodCode = "Cash",
            IsEnabled = true
        });
        var valid = Bill(restaurantId, paid: true, lines: 2);
        db.Payments.Add(Payment(valid, current, "Paid"));
        db.Payments.Add(Payment(Bill(restaurantId, paid: true), previous, "Paid"));
        db.Payments.Add(Payment(Bill(restaurantId, paid: true), current, "PendingHandover"));
        db.Payments.Add(Payment(Bill(Guid.NewGuid(), paid: true), current, "Paid"));
        await db.SaveChangesAsync();

        var rows = await new BillCheckoutRepository(db).GetPaidCandidatesAsync(restaurantId,
            register.CashRegisterId, current.CashShiftId, default);
        var card = Assert.Single(rows);
        Assert.Equal(valid.BillId, card.BillId);
        Assert.Equal(valid.BillNumber, card.BillCode);
        Assert.Equal(valid.TotalAmount, card.TotalAmount);
        Assert.NotNull(card.PaidAt);
    }

    [BillingSqlServerFact]
    public async Task IncomingCardsMergeRoutedLegacyBillAndKeepMissingBillAmountsNull()
    {
        await using var db = Context();
        var restaurantId = Guid.NewGuid();
        var register = new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Code = "DEFAULT"
        };
        var bill = Bill(restaurantId, paid: false, lines: 2);
        db.Bills.Add(bill);
        db.Bills.Add(Bill(restaurantId, paid: true));
        var routed = Checkout(register, bill.BillLines.First().TableSessionRef);
        var unbilled = Checkout(register, Guid.NewGuid());
        db.BillCheckouts.AddRange(routed, unbilled);
        await db.SaveChangesAsync();

        var rows = await new BillCheckoutRepository(db).GetIncomingCandidatesAsync(restaurantId, register.CashRegisterId, default);
        Assert.Equal(2, rows.Count);
        var billed = Assert.Single(rows, row => row.BillId == bill.BillId);
        Assert.Equal(routed.CheckoutId, billed.CheckoutId);
        Assert.Equal(bill.TotalAmount, billed.TotalAmount);
        Assert.Equal(bill.BillNumber, billed.BillCode);
        var missing = Assert.Single(rows, row => row.CheckoutId == unbilled.CheckoutId);
        Assert.Null(missing.BillId);
        Assert.Null(missing.BillCode);
        Assert.Null(missing.TotalAmount);
    }

    private static CashShift Shift(CashRegister register, bool closed)
    {
        return new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegister = register,
            CashierUserRef = Guid.NewGuid(),
            OpenedAt = DateTime.UtcNow.AddHours(-2),
            ClosedAt = closed ? DateTime.UtcNow.AddHours(-1) : null,
            Status = closed ? "Closed" : "Active"
        };
    }

    private static Bill Bill(Guid restaurantId, bool paid, int lines = 1)
    {
        var bill = new Bill
        {
            BillId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BillNumber = Guid.NewGuid().ToString("N"),
            Status = paid ? "Paid" : "Unpaid",
            PaidAt = paid ? DateTime.UtcNow : null,
            Currency = "VND",
            TotalAmount = 100
        };
        var session = Guid.NewGuid();
        for (var index = 0; index < lines; index++)
        {
            bill.BillLines.Add(new BillLine
            {
                BillLineId = Guid.NewGuid(),
                TableSessionRef = session,
                OriginalTableRef = Guid.NewGuid(),
                OrderItemRef = Guid.NewGuid(),
                ItemSnapshot = "{}",
                Quantity = 1,
                UnitPriceSnapshot = 100
            });
        }

        return bill;
    }

    private static Payment Payment(Bill bill, CashShift shift, string status)
    {
        return new Payment
        {
            PaymentId = Guid.NewGuid(),
            Bill = bill,
            CashShift = shift,
            MethodCode = "Cash",
            Status = status,
            PaidAt = status == "Paid" ? DateTime.UtcNow : null,
            Amount = bill.TotalAmount,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
    }

    private static BillCheckout Checkout(CashRegister register, Guid session)
    {
        return new BillCheckout
        {
            CheckoutId = Guid.NewGuid(),
            TableSessionRef = session,
            CashRegister = register,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = "Submitted"
        };
    }

    private BillingDbContext Context()
    {
        return new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(connection).Options);
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var sqlConnection = new SqlConnection(connectionString);
        await sqlConnection.OpenAsync();
        await using var command = new SqlCommand(sql, sqlConnection);
        await command.ExecuteNonQueryAsync();
    }
}
