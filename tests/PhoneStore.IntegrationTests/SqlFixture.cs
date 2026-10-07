using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhoneStore.Api.Data;

namespace PhoneStore.IntegrationTests;

public sealed class SqlFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    private string database = "";
    private string master = "";
    private bool created;

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public Task InitializeAsync() => InitializeAtAsync();

    public async Task InitializeAtAsync(string? migration = null)
    {
        var raw = Environment.GetEnvironmentVariable("PHONESTORE_TEST_SQL")
            ?? throw new InvalidOperationException("Set PHONESTORE_TEST_SQL to a local/CI SQL Server connection with a PhoneStore_Test_ database. SQL tests never silently skip.");
        var builder = new SqlConnectionStringBuilder(raw);
        if (!builder.InitialCatalog.StartsWith("PhoneStore_Test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing a connection outside PhoneStore_Test_ databases.");
        database = "PhoneStore_Test_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = "master";
        master = builder.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"CREATE DATABASE [{database}]", connection);
        await command.ExecuteNonQueryAsync();
        created = true;
        builder.InitialCatalog = database;
        ConnectionString = builder.ConnectionString;
        // Tests use explicit migrations. Production database is never recreated.
        await using var context = CreateContext();
        try
        {
            if (migration is null) await context.Database.MigrateAsync();
            else await context.GetService<IMigrator>().MigrateAsync(migration);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(database, "^PhoneStore_Test_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Unsafe cleanup target.");
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", connection);
        await command.ExecuteNonQueryAsync();
        created = false;
    }
}

[CollectionDefinition("SQL")]
public sealed class SqlCollection : ICollectionFixture<SqlFixture> { }


