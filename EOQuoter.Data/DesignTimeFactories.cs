using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EOQuoter.Data;

// Design-time factories so `dotnet ef migrations add` works against the Data project alone.
// The connection strings here are never used at runtime — hosts configure their own.

public class QuoterDbContextFactory : IDesignTimeDbContextFactory<QuoterDbContext>
{
    public QuoterDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<QuoterDbContext>()
            .UseSqlServer("Server=localhost;Database=EOQuoter;TrustServerCertificate=True")
            .Options);
}

public class ExecutionTrackingDbContextFactory : IDesignTimeDbContextFactory<ExecutionTrackingDbContext>
{
    public ExecutionTrackingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ExecutionTrackingDbContext>()
            .UseSqlServer("Server=localhost;Database=EOQuoterTracking;TrustServerCertificate=True")
            .Options);
}
