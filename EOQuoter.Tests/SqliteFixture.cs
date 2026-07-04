using EOQuoter.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOQuoter.Tests;

/// <summary>
/// SQLite-backed QuoterDbContext for tests that exercise EF-level behavior (temporal queries,
/// idempotency claims, outbox) without a SQL Server. The unique-constraint semantics these tests
/// rely on hold on both engines. Sproc behavior is NOT testable here — that's SqlServerFact tests.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<QuoterDbContext> options;

    public SqliteFixture()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<QuoterDbContext>().UseSqlite(connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public QuoterDbContext CreateContext() => new(options);

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;

    public void Dispose() => connection.Dispose();
}
