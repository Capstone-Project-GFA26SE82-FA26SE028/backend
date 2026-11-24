using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Foundation.Tests;

public class UnicodePersistenceTests
{
    public static IEnumerable<object[]> Columns()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UnicodeText.sql"));
        var matches = Regex.Matches(sql, @"\(N'(?<db>\w+)',N'(?<table>\w+)',N'(?<column>\w+)',(?<size>-?\d+),(?<nullable>[01])\)");
        Assert.Equal(56, matches.Count);
        foreach (Match match in matches)
        {
            yield return new object[]
            {
                match.Groups["db"].Value,
                match.Groups["table"].Value,
                match.Groups["column"].Value,
                int.Parse(match.Groups["size"].Value),
                match.Groups["nullable"].Value == "1"
            };
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public void TextMappingPreservesUnicode(string database, string table, string column, int size, bool nullable)
    {
        using var context = CreateContext(database);
        var entity = Assert.Single(context.Model.GetEntityTypes(), value => value.GetTableName() == table);
        var identifier = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var property = Assert.Single(entity.GetProperties(), value => value.GetColumnName(identifier) == column);
        Assert.NotEqual(false, property.IsUnicode());
        Assert.Equal(size == -1 ? "nvarchar(max)" : $"nvarchar({size})", property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
    }

    [SqlServerTheory]
    [MemberData(nameof(Columns))]
    public async Task SqlServerParameterPreservesVietnamese(string database, string table, string column, int size, bool nullable)
    {
        var connectionString = Environment.GetEnvironmentVariable("UNICODE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("An isolated SQL connection is required.");
        }

        using var context = CreateContext(database);
        var entity = Assert.Single(context.Model.GetEntityTypes(), value => value.GetTableName() == table);
        var identifier = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var property = Assert.Single(entity.GetProperties(), value => value.GetColumnName(identifier) == column);
        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(size == -1 ? "nvarchar(max)" : $"nvarchar({size})", property.GetColumnType());
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        const string text = "Phở bò, gạo, ít cay – Đặng";
        command.CommandText = "CREATE TABLE #UnicodeProbe(Value nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS); INSERT #UnicodeProbe VALUES (@value); SELECT Value FROM #UnicodeProbe; DROP TABLE #UnicodeProbe;";
        command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, "@value", text));
        Assert.Equal(text, await command.ExecuteScalarAsync());
    }

    private static DbContext CreateContext(string database)
    {
        const string connection = "Server=localhost;Database=MetadataOnly;Integrated Security=True;TrustServerCertificate=True";
        return database switch
        {
            "IdentityDb" => new Identity.Domain.Models.IdentityDbContext(new DbContextOptionsBuilder<Identity.Domain.Models.IdentityDbContext>().UseSqlServer(connection).Options),
            "RestaurantDb" => new Restaurant.DAL.Models.RestaurantDbContext(new DbContextOptionsBuilder<Restaurant.DAL.Models.RestaurantDbContext>().UseSqlServer(connection).Options),
            "CatalogDb" => new Catalog.DAL.Models.CatalogDbContext(new DbContextOptionsBuilder<Catalog.DAL.Models.CatalogDbContext>().UseSqlServer(connection).Options),
            "InventoryDb" => new Inventory.DAL.Models.InventoryDbContext(new DbContextOptionsBuilder<Inventory.DAL.Models.InventoryDbContext>().UseSqlServer(connection).Options),
            "OrderingDb" => new Ordering.DAL.Models.OrderingDbContext(new DbContextOptionsBuilder<Ordering.DAL.Models.OrderingDbContext>().UseSqlServer(connection).Options),
            "CustomerDb" => new Customer.DAL.Models.CustomerDbContext(new DbContextOptionsBuilder<Customer.DAL.Models.CustomerDbContext>().UseSqlServer(connection).Options),
            "BillingDb" => new Billing.DAL.Models.BillingDbContext(new DbContextOptionsBuilder<Billing.DAL.Models.BillingDbContext>().UseSqlServer(connection).Options),
            _ => throw new ArgumentOutOfRangeException(nameof(database))
        };
    }
}

public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNICODE_TEST_CONNECTION")))
        {
            Skip = "Run scripts/Test-UnicodeDockerBootstrap.ps1 -VerifyEf for real SQL parameter tests.";
        }
    }
}
