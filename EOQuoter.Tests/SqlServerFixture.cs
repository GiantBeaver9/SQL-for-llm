using EOQuoter.Data;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Tests;

/// <summary>
/// Runs only when EOQUOTER_TEST_SQLSERVER is set (e.g. "Server=localhost,1433;User Id=sa;
/// Password=…;TrustServerCertificate=True"). Sprocs are T-SQL — SQLite can't stand in for them.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EOQUOTER_TEST_SQLSERVER") is null or "")
            Skip = "Set EOQUOTER_TEST_SQLSERVER to a SQL Server connection string to run sproc tests";
    }
}

/// <summary>Scratch database per fixture: migrate, install the checked-in sprocs, drop on dispose.</summary>
public sealed class SqlServerFixture : IDisposable
{
    private readonly string dbName = $"EOQuoterTest_{Guid.NewGuid():N}";
    private readonly DbContextOptions<QuoterDbContext> options;

    public SqlServerFixture()
    {
        var server = Environment.GetEnvironmentVariable("EOQUOTER_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("EOQUOTER_TEST_SQLSERVER not set");
        options = new DbContextOptionsBuilder<QuoterDbContext>()
            .UseSqlServer($"{server.TrimEnd(';')};Database={dbName}")
            .Options;

        using var db = CreateContext();
        db.Database.Migrate();
        SqlObjectInstaller.ApplyAsync(db).GetAwaiter().GetResult();
    }

    public QuoterDbContext CreateContext() => new(options);

    public void Dispose()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }
}
