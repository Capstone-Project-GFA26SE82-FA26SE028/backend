using Microsoft.Data.SqlClient;

namespace Billing.Tests;

public sealed class BillingSqlServerFactAttribute : FactAttribute
{
    public BillingSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BILLING_SQL_TEST_CONNECTION")))
        {
            Skip = "Set BILLING_SQL_TEST_CONNECTION to a SQL Server connection with permission to create isolated test databases.";
        }
    }
}

public sealed class CashRegisterUpgradeSqlTests : IAsyncLifetime
{
    private const string DatabasePrefix = "CodexBillingUpgrade_";
    private readonly string _databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
    private string _masterConnection = string.Empty;
    private string _testConnection = string.Empty;
    private string _upgrade = string.Empty;
    private bool _databaseCreated;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BILLING_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        _upgrade = await File.ReadAllTextAsync(FindUpgradeScript());
        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = "master",
            Pooling = false
        };
        _masterConnection = builder.ConnectionString;
        builder.InitialCatalog = _databaseName;
        _testConnection = builder.ConnectionString;
        RequireTestDatabaseName();
        await Execute(_masterConnection, $"CREATE DATABASE [{_databaseName}];");
        _databaseCreated = true;
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated)
        {
            return;
        }

        RequireTestDatabaseName();
        await Execute(_masterConnection,
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];");
    }

    [BillingSqlServerFact]
    public async Task EmptyLegacyTableCanUpgradeTwiceWithoutAddingDefaultsOrForeignKeys()
    {
        await LegacyTable();
        await Upgrade();
        await Upgrade();

        Assert.Equal("uniqueidentifier", await Scalar("SELECT TYPE_NAME(system_type_id) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
        Assert.Equal(false, await Scalar("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) FROM dbo.CashRegister;"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.CashRegister');"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.CashRegister');"));
    }

    [BillingSqlServerFact]
    public async Task ExistingLegacyRowsRequireMappingAndLeaveOriginalSchemaAndDataUnchanged()
    {
        await LegacyTable();
        var registerId = Guid.NewGuid();
        await Execute(_testConnection,
            "INSERT dbo.CashRegister (CashRegisterId, Code) VALUES (@RegisterId, 'EXISTING');",
            new SqlParameter("@RegisterId", registerId));

        var error = await Assert.ThrowsAsync<SqlException>(Upgrade);
        Assert.Equal(50001, error.Number);
        Assert.Contains("explicit CashRegisterId-to-RestaurantId mapping", error.Message);
        Assert.Equal(DBNull.Value, await Scalar("SELECT COL_LENGTH(N'dbo.CashRegister', N'RestaurantId');"));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM dbo.CashRegister;"));
        Assert.Equal("EXISTING", await Scalar("SELECT Code FROM dbo.CashRegister;"));
        Assert.Equal(registerId, await Scalar("SELECT CashRegisterId FROM dbo.CashRegister;"));
    }

    [BillingSqlServerFact]
    public async Task ExplicitNullableMappingsBecomeRequiredWithoutChangingTheirValues()
    {
        var registerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        await NullableTable();
        await Execute(_testConnection,
            "INSERT dbo.CashRegister (CashRegisterId, RestaurantId, Code) VALUES (@RegisterId, @RestaurantId, 'MAPPED');",
            new SqlParameter("@RegisterId", registerId),
            new SqlParameter("@RestaurantId", restaurantId));

        await Upgrade();
        await Upgrade();

        Assert.Equal(false, await Scalar("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
        Assert.Equal(registerId, await Scalar("SELECT CashRegisterId FROM dbo.CashRegister;"));
        Assert.Equal(restaurantId, await Scalar("SELECT RestaurantId FROM dbo.CashRegister;"));
        Assert.Equal("MAPPED", await Scalar("SELECT Code FROM dbo.CashRegister;"));
    }

    [BillingSqlServerFact]
    public async Task UnmappedNullableRowsAreRejectedWithoutFillingOrDroppingThem()
    {
        await NullableTable();
        await Execute(_testConnection,
            "INSERT dbo.CashRegister (CashRegisterId, RestaurantId, Code) VALUES (NEWID(), NULL, 'UNMAPPED');");

        var error = await Assert.ThrowsAsync<SqlException>(Upgrade);
        Assert.Equal(50001, error.Number);
        Assert.Contains("explicit mapping", error.Message);
        Assert.Equal(true, await Scalar("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM dbo.CashRegister WHERE RestaurantId IS NULL AND Code = 'UNMAPPED';"));
    }

    [BillingSqlServerFact]
    public async Task EmptyGuidIsNotAcceptedAsABranchMapping()
    {
        await NullableTable();
        await Execute(_testConnection,
            "INSERT dbo.CashRegister (CashRegisterId, RestaurantId, Code) VALUES (NEWID(), @RestaurantId, 'EMPTY');",
            new SqlParameter("@RestaurantId", Guid.Empty));

        var error = await Assert.ThrowsAsync<SqlException>(Upgrade);
        Assert.Equal(50001, error.Number);
        Assert.Equal(Guid.Empty, await Scalar("SELECT RestaurantId FROM dbo.CashRegister;"));
        Assert.Equal(true, await Scalar("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
    }

    [BillingSqlServerFact]
    public async Task UnexpectedReferenceTypeIsReportedWithoutAlteringTheColumn()
    {
        await Execute(_testConnection,
            "CREATE TABLE dbo.CashRegister (CashRegisterId uniqueidentifier NOT NULL PRIMARY KEY, RestaurantId varchar(50) NULL, Code varchar(50) NOT NULL);");

        var error = await Assert.ThrowsAsync<SqlException>(Upgrade);
        Assert.Equal(50001, error.Number);
        Assert.Contains("non-computed uniqueidentifier", error.Message);
        Assert.Equal("varchar", await Scalar("SELECT TYPE_NAME(system_type_id) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashRegister') AND name = N'RestaurantId';"));
    }

    private Task LegacyTable()
    {
        return Execute(_testConnection,
            "CREATE TABLE dbo.CashRegister (CashRegisterId uniqueidentifier NOT NULL PRIMARY KEY, Code varchar(50) NOT NULL);");
    }

    private Task NullableTable()
    {
        return Execute(_testConnection,
            "CREATE TABLE dbo.CashRegister (CashRegisterId uniqueidentifier NOT NULL PRIMARY KEY, RestaurantId uniqueidentifier NULL, Code varchar(50) NOT NULL);");
    }

    private Task Upgrade()
    {
        RequireTestDatabaseName();
        return Execute(_testConnection, _upgrade);
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqlConnection(_testConnection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task Execute(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private void RequireTestDatabaseName()
    {
        if (!_databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(_databaseName[DatabasePrefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Only an isolated Billing upgrade test database may be initialized or removed.");
        }
    }

    private static string FindUpgradeScript()
    {
        var copiedScript = Path.Combine(AppContext.BaseDirectory, "Sql", "BillingCashRegisterRestaurant.sql");
        if (File.Exists(copiedScript))
        {
            return copiedScript;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, "docker", "sql", "upgrades", "BillingCashRegisterRestaurant.sql");
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Run the opt-in SQL tests from a repository checkout containing docker/sql/upgrades/BillingCashRegisterRestaurant.sql.");
    }
}
