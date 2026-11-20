using System.Text.RegularExpressions;

namespace Milkshake.UnitTests.Auditing;

/// <summary>
/// M11, AC-11.5: the audit interceptor sees only writes made through EF Core change tracking. This scan fails if the
/// application code gains any other way to write – raw SQL, bulk ExecuteUpdate or ExecuteDelete – that has not been
/// reviewed and listed here. Migrations change the schema, not data, and are excluded.
/// </summary>
public class AuditBypassScanTests
{
    private static readonly Regex Bypass = new(
        @"\b(ExecuteUpdate|ExecuteDelete)(Async)?\s*\(|\bExecuteSql(Raw|Interpolated)?(Async)?\s*\(|\bSqlQuery(Raw)?\s*<|\bFromSql(Raw|Interpolated)?\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>Reviewed uses: file name → how many, and why they write nothing.</summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Reviewed = new(StringComparer.Ordinal)
    {
        ["DatabaseHealthCheck.cs"] = (1, "SELECT 1 (health check)"),
        ["EmailDispatcher.cs"] = (1, "SELECT … FOR UPDATE SKIP LOCKED: locks due emails; the claim itself is an audited save"),
    };

    [Fact]
    public void The_application_has_no_unreviewed_write_that_bypasses_the_audit()
    {
        string src = Path.Combine(RepositoryRoot(), "backend", "src");
        Dictionary<string, int> found = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .Select(path => (Name: Path.GetFileName(path), Count: Bypass.Matches(File.ReadAllText(path)).Count))
            .Where(file => file.Count > 0)
            .GroupBy(file => file.Name)
            .ToDictionary(group => group.Key, group => group.Sum(file => file.Count), StringComparer.Ordinal);

        List<string> unreviewed = found
            .Where(file => !Reviewed.TryGetValue(file.Key, out (int Count, string Reason) allowed) || allowed.Count != file.Value)
            .Select(file => $"{file.Key}: {file.Value} use(s)")
            .ToList();
        Assert.Empty(unreviewed);
    }

    [Fact]
    public void The_reviewed_raw_queries_only_read()
    {
        string src = Path.Combine(RepositoryRoot(), "backend", "src");
        foreach (string name in Reviewed.Keys)
        {
            string path = Directory.EnumerateFiles(src, name, SearchOption.AllDirectories).Single(p => !IsExcluded(p));
            string code = File.ReadAllText(path);
            Assert.DoesNotMatch(@"(?i)\b(UPDATE\s+milkshake|INSERT\s+INTO|DELETE\s+FROM|TRUNCATE)\b", code);
        }
    }

    private static bool IsExcluded(string path)
    {
        string normalised = path.Replace('\\', '/');
        return normalised.Contains("/bin/", StringComparison.Ordinal) ||
               normalised.Contains("/obj/", StringComparison.Ordinal) ||
               normalised.Contains("/Migrations/", StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Milkshake.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (Milkshake.slnx) was not found.");
    }
}
