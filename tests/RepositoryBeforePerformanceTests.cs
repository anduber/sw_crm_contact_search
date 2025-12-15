using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using repository_before;
using Xunit;

public class RepositoryBeforePerformanceTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task RepositoryBefore_SearchContacts_UsesExistingDatabaseWithinBudget()
    {
        var dbPath = CreateTemporaryDatabasePath();
        Assert.False(File.Exists(dbPath), "Ephemeral CRM database should not exist before the run.");

        const int requestedPageSize = 25;
        var run = await ExecuteLegacySearchAsync(dbPath, requestedPageSize);

        var maxDuration = TimeSpan.FromSeconds(2);
        Assert.True(run.Elapsed < maxDuration,
            $"Search exceeded performance budget ({run.Elapsed.TotalSeconds:F1}s > {maxDuration.TotalSeconds}s).");

        Assert.True(run.DatabaseCreatedDuringExecution, "SQLite database should be created while the search runs.");

        var result = run.Result;
        Assert.True(result.TotalCount > 0, "Search returned no contacts despite seeded CRM data.");
        Assert.True(result.Data.Count <= requestedPageSize, "Search returned more contacts than requested page size.");
        Assert.True(result.ElapsedMilliseconds >= 0, "Search did not record elapsed milliseconds.");
        Assert.True(result.ElapsedMilliseconds <= run.Elapsed.TotalMilliseconds,
            "SearchResult elapsed time should not exceed the measured wall-clock time.");

        var fileExistsPostRun = File.Exists(dbPath);
        Assert.True(fileExistsPostRun, "SQLite database file should exist after executing the search.");
        SqliteConnection.ClearAllPools();
        CleanupDatabaseArtifacts(dbPath);
        Assert.False(File.Exists(dbPath), "Ephemeral SQLite database should be deleted during test cleanup.");
    }

    private static async Task<SearchRunResult> ExecuteLegacySearchAsync(string dbPath, int requestedPageSize)
    {
        await using var context = new CrmContactSearch.CrmDbContext(dbPath, deleteDatabaseOnDispose: true);
        await context.EnsureCreatedAndSeededAsync();
        Assert.True(File.Exists(dbPath), "SQLite file should exist after seeding.");
        Assert.True(await context.Database.CanConnectAsync(), "Unable to connect to CRM SQLite database.");

        var searcher = new CrmContactSearch.ContactSearcher(context);
        var request = new CrmContactSearch.ContactSearchRequest
        {
            MinDealValue = 10_000m,
            Page = 1,
            PageSize = requestedPageSize
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await searcher.SearchContacts(request);
        stopwatch.Stop();

        return new SearchRunResult(result, stopwatch.Elapsed, File.Exists(dbPath));
    }

    private static string CreateTemporaryDatabasePath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "crm_contact_search_tests");
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(tempDirectory, $"crm_contacts_{Guid.NewGuid():N}.sqlite");
    }

    private static void CleanupDatabaseArtifacts(string databasePath)
    {
        DeleteIfExists(databasePath);
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException)
            {
                if (attempt == maxAttempts)
                {
                    return;
                }

                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == maxAttempts)
                {
                    return;
                }

                Thread.Sleep(50);
            }
        }
    }

    private sealed record SearchRunResult(CrmContactSearch.SearchResult Result, TimeSpan Elapsed, bool DatabaseCreatedDuringExecution);
}
