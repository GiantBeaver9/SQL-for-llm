using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data;

/// <summary>
/// Applies the checked-in T-SQL objects (stored procedures) after EF migrations run. The .sql
/// files are embedded resources so the deployed binary carries exactly the sproc versions it was
/// built with. CREATE OR ALTER keeps application idempotent.
/// </summary>
public static class SqlObjectInstaller
{
    public static async Task ApplyAsync(QuoterDbContext db, CancellationToken ct = default)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql")).OrderBy(n => n))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync(ct);
            foreach (var batch in SplitBatches(script))
                await db.Database.ExecuteSqlRawAsync(batch, ct);
        }
    }

    // Split on GO lines — GO is a client-side batch separator, not T-SQL.
    private static IEnumerable<string> SplitBatches(string script)
    {
        var batch = new List<string>();
        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (batch.Count > 0) { yield return string.Join('\n', batch); batch.Clear(); }
            }
            else batch.Add(line);
        }
        if (batch.Any(l => !string.IsNullOrWhiteSpace(l))) yield return string.Join('\n', batch);
    }
}
