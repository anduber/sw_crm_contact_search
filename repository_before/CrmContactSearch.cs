using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.IO;
#nullable enable

namespace repository_before
{
    public class CrmContactSearch
    {
        public enum DealStage
        {
            Prospect = 0,
            Qualified = 1,
            Proposal = 2,
            Negotiation = 3,
            ClosedWon = 4,
            ClosedLost = 5
        }

        public enum InteractionType
        {
            Email = 0,
            Call = 1,
            Meeting = 2,
            Demo = 3,
            Other = 4
        }

        // Entities
        public class Tag
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int ContactId { get; set; }
            public Contact? Contact { get; set; }
        }

        public class Interaction
        {
            public int Id { get; set; }
            public int ContactId { get; set; }
            public InteractionType Type { get; set; }
            public DateTime Date { get; set; }
            public Contact? Contact { get; set; }
        }

        public class Deal
        {
            public int Id { get; set; }
            public int ContactId { get; set; }
            public decimal? EstimatedValue { get; set; }
            public Contact? Contact { get; set; }
        }

        public class Contact
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Company { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public DateTime LastContactDate { get; set; }
            public DealStage? DealStage { get; set; }
            public decimal? BasePotentialValue { get; set; }

            // Navigation properties
            public List<Tag> Tags { get; set; } = new List<Tag>();
            public List<Interaction> Interactions { get; set; } = new List<Interaction>();
            public List<Deal> Deals { get; set; } = new List<Deal>();
        }

        // DTOs
        public class ContactDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Company { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public DateTime LastContact { get; set; }
            public decimal DealValue { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public int InteractionCount { get; set; }
        }

        // Request model
        public class ContactSearchRequest
        {
            public string? City { get; set; }
            public string? Tags { get; set; }
            public DateTime? LastContactBefore { get; set; }
            public DealStage? DealStage { get; set; }
            public decimal? MinDealValue { get; set; }
            public int? Page { get; set; }
            public int? PageSize { get; set; }
        }


        // SQLite DbContext with mock data generation
        public class CrmDbContext : DbContext
        {
            private readonly string _databasePath;
            private readonly bool _ownsDatabaseFile;
            private readonly EventHandler? _processExitHandler;
            private bool _databaseCleanupCompleted;

            public DbSet<Contact> Contacts => Set<Contact>();
            public DbSet<Tag> Tags => Set<Tag>();
            public DbSet<Interaction> Interactions => Set<Interaction>();
            public DbSet<Deal> Deals => Set<Deal>();

            public CrmDbContext(string? databasePath = null, bool deleteDatabaseOnDispose = true)
            {
                var defaultDatabasePath = Path.Combine(AppContext.BaseDirectory, "crm_contacts.sqlite");
                _databasePath = databasePath ?? defaultDatabasePath;
                _ownsDatabaseFile = deleteDatabaseOnDispose && databasePath is null;
                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_ownsDatabaseFile)
                {
                    _processExitHandler = OnProcessExit;
                    AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
                }
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                if (!optionsBuilder.IsConfigured)
                {
                    optionsBuilder.UseSqlite($"Data Source={_databasePath}");
                }
            }

            public async Task EnsureCreatedAndSeededAsync()
            {
                await Database.EnsureCreatedAsync();

                if (!await Contacts.AnyAsync())
                {
                    await GenerateMockData();
                }
            }

            public async Task<List<Contact>> GetAllContactsAsync()
            {
                return await Contacts
                    .Include(c => c.Tags)
                    .Include(c => c.Interactions)
                    .Include(c => c.Deals)
                    .AsNoTracking()
                    .ToListAsync();
            }

            private async Task GenerateMockData()
            {
                const int totalContacts = 50000;
                var random = new Random(42);
                var cities = new[] { "New York", "London", "Tokyo", "Paris", "Berlin", "Sydney", "Toronto", "Singapore" };
                var tagNames = new[] { "VIP", "Regular", "New", "Active", "Inactive", "Premium", "Enterprise", "SMB" };

                var contacts = new List<Contact>(totalContacts);

                for (int i = 1; i <= totalContacts; i++)
                {
                    var contact = new Contact
                    {
                        Id = i,
                        FirstName = $"First{i}",
                        LastName = $"Last{i}",
                        Email = $"contact{i}@example.com",
                        Company = $"Company {i % 100}",
                        City = cities[random.Next(cities.Length)],
                        LastContactDate = DateTime.Now.AddDays(-random.Next(1, 730)),
                        DealStage = (DealStage)random.Next(0, 6),
                        BasePotentialValue = random.Next(1000, 100000)
                    };

                    var tagCount = random.Next(1, 4);
                    for (int j = 0; j < tagCount; j++)
                    {
                        contact.Tags.Add(new Tag
                        {
                            Name = tagNames[random.Next(tagNames.Length)]
                        });
                    }

                    var interactionCount = random.Next(1, 6);
                    for (int j = 0; j < interactionCount; j++)
                    {
                        contact.Interactions.Add(new Interaction
                        {
                            Type = (InteractionType)random.Next(0, 5),
                            Date = DateTime.Now.AddDays(-random.Next(1, 365))
                        });
                    }

                    var dealCount = random.Next(0, 4);
                    for (int j = 0; j < dealCount; j++)
                    {
                        contact.Deals.Add(new Deal
                        {
                            EstimatedValue = random.Next(5000, 500000)
                        });
                    }

                    contacts.Add(contact);
                }

                await Contacts.AddRangeAsync(contacts);
                await SaveChangesAsync();
            }

            public override void Dispose()
            {
                base.Dispose();
                CleanupDatabaseFile();
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();
                CleanupDatabaseFile();
            }

            private void OnProcessExit(object? sender, EventArgs e)
            {
                CleanupDatabaseFile();
            }

            private void CleanupDatabaseFile()
            {
                if (!_ownsDatabaseFile || _databaseCleanupCompleted)
                {
                    return;
                }

                try
                {
                    if (File.Exists(_databasePath))
                    {
                        File.Delete(_databasePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Unable to delete SQLite database at '{_databasePath}': {ex.Message}");
                }
                finally
                {
                    if (_processExitHandler is not null)
                    {
                        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
                    }

                    _databaseCleanupCompleted = true;
                }
            }
        }

        // Main search service (converted from API controller)
        public class ContactSearcher
        {
            private readonly CrmDbContext _context;

            public ContactSearcher(CrmDbContext context)
            {
                _context = context;
            }

            public async Task<SearchResult> SearchContacts(ContactSearchRequest request)
            {
                Console.WriteLine("Loading ALL contacts from the SQLite database into memory...");

                // Load ALL contacts into memory (simulating the inefficient ToListAsync())
                var allContacts = await _context.GetAllContactsAsync();

                Console.WriteLine($"Loaded {allContacts.Count:N0} contacts into memory");

                var filteredContacts = allContacts;

                // Sequential filtering in memory (not database)
                Console.WriteLine("\nStarting sequential in-memory filtering...");

                var startTime = DateTime.Now;

                if (!string.IsNullOrEmpty(request.City))
                {
                    Console.WriteLine($"Filtering by City: {request.City}");
                    filteredContacts = filteredContacts
                        .Where(c => c.City == request.City)
                        .ToList();
                    Console.WriteLine($"    Contacts after city filter: {filteredContacts.Count:N0}");
                }

                if (!string.IsNullOrEmpty(request.Tags))
                {
                    Console.WriteLine($"Filtering by Tags: {request.Tags}");
                    var tagList = request.Tags.Split(',');
                    filteredContacts = filteredContacts
                        .Where(c => c.Tags.Any(t => tagList.Contains(t.Name)))
                        .ToList();
                    Console.WriteLine($"    Contacts after tags filter: {filteredContacts.Count:N0}");
                }

                if (request.LastContactBefore.HasValue)
                {
                    Console.WriteLine($"Filtering by LastContactBefore: {request.LastContactBefore.Value:yyyy-MM-dd}");
                    filteredContacts = filteredContacts
                        .Where(c => c.LastContactDate < request.LastContactBefore.Value)
                        .ToList();
                    Console.WriteLine($"    Contacts after date filter: {filteredContacts.Count:N0}");
                }

                if (request.DealStage.HasValue)
                {
                    Console.WriteLine($"  Filtering by DealStage: {request.DealStage.Value}");
                    filteredContacts = filteredContacts
                        .Where(c => c.DealStage == request.DealStage.Value)
                        .ToList();
                    Console.WriteLine($"    Contacts after deal stage filter: {filteredContacts.Count:N0}");
                }

                // Complex custom filtering that can't use indexes
                if (request.MinDealValue.HasValue)
                {
                    Console.WriteLine($"Filtering by MinDealValue: {request.MinDealValue.Value:C}");
                    Console.WriteLine("Starting expensive deal value calculations...");
                    filteredContacts = filteredContacts
                        .Where(c => CalculatePotentialDealValue(c) > request.MinDealValue.Value)
                        .ToList();
                    Console.WriteLine($"Contacts after deal value filter: {filteredContacts.Count:N0}");
                }

                // Sorting in memory
                Console.WriteLine($"\n Sorting {filteredContacts.Count:N0} contacts in memory...");
                var sortedContacts = filteredContacts
                    .OrderByDescending(c => c.LastContactDate)
                    .ThenBy(c => c.LastName)
                    .ThenBy(c => c.FirstName)
                    .ToList();

                // Pagination after processing everything
                var page = request.Page ?? 1;
                var pageSize = request.PageSize ?? 50;
                var startIndex = (page - 1) * pageSize;

                Console.WriteLine($"\n Paginating (page {page}, size {pageSize})...");
                var pagedContacts = sortedContacts
                    .Skip(startIndex)
                    .Take(pageSize)
                    .ToList();

                // Additional processing for response
                Console.WriteLine($"\n Processing {pagedContacts.Count} contacts for display...");
                var result = pagedContacts.Select(c => new ContactDto
                {
                    Id = c.Id,
                    FullName = $"{c.FirstName} {c.LastName}",
                    Email = c.Email,
                    Company = c.Company,
                    City = c.City,
                    LastContact = c.LastContactDate,
                    DealValue = CalculatePotentialDealValue(c), // Expensive calculation
                    Tags = c.Tags.Select(t => t.Name).ToList(),
                    InteractionCount = c.Interactions.Count
                }).ToList();

                var elapsed = DateTime.Now - startTime;

                return new SearchResult
                {
                    TotalCount = sortedContacts.Count,
                    Page = page,
                    PageSize = pageSize,
                    Data = result,
                    ElapsedMilliseconds = elapsed.TotalMilliseconds
                };
            }

            // Expensive calculation that can't be indexed
            private decimal CalculatePotentialDealValue(Contact contact)
            {
                // Simulate complex business logic with multiple "database" lookups
                var interactions = _context.Interactions
                    .Where(i => i.ContactId == contact.Id)
                    .ToList();

                var deals = _context.Deals
                    .Where(d => d.ContactId == contact.Id)
                    .ToList();

                decimal baseValue = contact.BasePotentialValue ?? 0;

                // Weighted calculations based on interactions
                foreach (var interaction in interactions)
                {
                    baseValue *= GetInteractionMultiplier(interaction.Type);
                }

                // Adjust based on deal stage
                baseValue *= GetStageMultiplier(contact.DealStage);

                // Add deal values
                baseValue += deals.Sum(d => d.EstimatedValue ?? 0);

                // Apply time decay for old contacts
                var daysSinceLastContact = (DateTime.Now - contact.LastContactDate).Days;
                if (daysSinceLastContact > 30)
                {
                    baseValue *= (decimal)Math.Pow(0.99, daysSinceLastContact - 30);
                }

                return Math.Round(baseValue, 2);
            }

            private decimal GetInteractionMultiplier(InteractionType type)
            {
                return type switch
                {
                    InteractionType.Email => 1.01m,
                    InteractionType.Call => 1.05m,
                    InteractionType.Meeting => 1.15m,
                    InteractionType.Demo => 1.25m,
                    _ => 1.0m
                };
            }

            private decimal GetStageMultiplier(DealStage? stage)
            {
                return stage switch
                {
                    DealStage.Prospect => 0.3m,
                    DealStage.Qualified => 0.6m,
                    DealStage.Proposal => 0.8m,
                    DealStage.Negotiation => 0.9m,
                    DealStage.ClosedWon => 1.0m,
                    DealStage.ClosedLost => 0.0m,
                    _ => 0.1m
                };
            }
        }

        public class SearchResult
        {
            public int TotalCount { get; set; }
            public int Page { get; set; }
            public int PageSize { get; set; }
            public List<ContactDto> Data { get; set; } = new List<ContactDto>();
            public double ElapsedMilliseconds { get; set; }
        }
    }
}
