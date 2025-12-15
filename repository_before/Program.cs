using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections;
using System.IO;
using static repository_before.CrmContactSearch;

namespace RepositoryBefore;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("==========================================");
        Console.WriteLine(" INEFFICIENT CONTACT SEARCH CONSOLE APP");
        Console.WriteLine("==========================================");
        Console.WriteLine("This demonstrates POOR performance patterns:");
        Console.WriteLine("    • Loads ALL data into memory");
        Console.WriteLine("    • Filters in-memory instead of database");
        Console.WriteLine("    • Expensive per-record calculations");
        Console.WriteLine("    • Processes everything before pagination");
        Console.WriteLine("==========================================\n");

        // Initialize
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var dataFolder = Path.Combine(projectRoot, "CrmData");
        Directory.CreateDirectory(dataFolder);
        var databasePath = Path.Combine(dataFolder, "crm_contacts.sqlite");
        await using var dbContext = new CrmDbContext(databasePath);
        await dbContext.EnsureCreatedAndSeededAsync();


        var ll = await dbContext.GetAllContactsAsync();
        var searcher = new ContactSearcher(dbContext);

        bool continueSearching = true;

        while (continueSearching)
        {
            Console.WriteLine("\n=== Enter Search Criteria ===\n");

            var request = new ContactSearchRequest();

            Console.Write("City (press Enter to skip): ");
            request.City = Console.ReadLine();

            Console.Write("Tags (comma-separated, press Enter to skip): ");
            request.Tags = Console.ReadLine();

            Console.Write("Last contact before (yyyy-mm-dd, press Enter to skip): ");
            var dateInput = Console.ReadLine();
            if (DateTime.TryParse(dateInput, out var date))
            {
                request.LastContactBefore = date;
            }

            Console.Write("Deal Stage (0-5, press Enter to skip): ");
            Console.WriteLine("  0: Prospect, 1: Qualified, 2: Proposal, 3: Negotiation, 4: ClosedWon, 5: ClosedLost");
            var stageInput = Console.ReadLine();
            if (int.TryParse(stageInput, out int stage) && Enum.IsDefined(typeof(DealStage), stage))
            {
                request.DealStage = (DealStage)stage;
            }

            Console.Write("Minimum Deal Value (press Enter to skip): ");
            var valueInput = Console.ReadLine();
            if (decimal.TryParse(valueInput, out decimal minValue))
            {
                request.MinDealValue = minValue;
            }

            Console.Write("Page number (default 1): ");
            var pageInput = Console.ReadLine();
            if (int.TryParse(pageInput, out int page))
            {
                request.Page = page;
            }

            Console.Write("Page size (default 50): ");
            var sizeInput = Console.ReadLine();
            if (int.TryParse(sizeInput, out int size))
            {
                request.PageSize = size;
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("STARTING INEFFICIENT SEARCH...");
            Console.WriteLine(new string('=', 50));

            try
            {
                var result = await searcher.SearchContacts(request);

                Console.WriteLine("\n" + new string('=', 50));
                Console.WriteLine("SEARCH COMPLETE");
                Console.WriteLine(new string('=', 50));

                Console.WriteLine($"\n RESULTS SUMMARY:");
                Console.WriteLine($"   Total matching contacts: {result.TotalCount:N0}");
                Console.WriteLine($"   Page: {result.Page} of {Math.Ceiling(result.TotalCount / (double)result.PageSize):N0}");
                Console.WriteLine($"   Showing: {result.Data.Count} contacts");
                Console.WriteLine($"   Time elapsed: {result.ElapsedMilliseconds:F2} ms");

                Console.WriteLine($"\n💀 MEMORY & PERFORMANCE ISSUES:");
                Console.WriteLine($"   • Loaded ALL contacts before filtering");
                Console.WriteLine($"   • Multiple ToList() calls created new collections");
                Console.WriteLine($"   • Sorting done in memory on {result.TotalCount:N0} records");
                Console.WriteLine($"   • Pagination applied AFTER full processing");
                Console.WriteLine($"   • Expensive deal value calculation for each contact");

                Console.WriteLine($"\n📄 PAGE {result.Page} RESULTS:");
                foreach (var contact in result.Data.Take(10))
                {
                    Console.WriteLine($"\n  {contact.FullName}");
                    Console.WriteLine($" Email: {contact.Email}");
                    Console.WriteLine($" Company: {contact.Company}");
                    Console.WriteLine($" City: {contact.City}");
                    Console.WriteLine($" Last Contact: {contact.LastContact:yyyy-MM-dd}");
                    Console.WriteLine($" Deal Value: {contact.DealValue:C}");
                    Console.WriteLine($" Tags: {string.Join(", ", contact.Tags)}");
                    Console.WriteLine($" Interactions: {contact.InteractionCount}");
                }

                if (result.Data.Count > 10)
                {
                    Console.WriteLine($"\n  ... and {result.Data.Count - 10} more contacts");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
            }

            Console.Write("\n\nSearch again? (y/n): ");
            continueSearching = Console.ReadLine()?.ToLower() == "y";
        }

        Console.WriteLine("\n👋 Goodbye!");
    }


}
