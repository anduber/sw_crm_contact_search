using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

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

        // Entities (keep structure close to your original; add Metrics for cached calculations)
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

            public List<Tag> Tags { get; set; } = new();
            public List<Interaction> Interactions { get; set; } = new();
            public List<Deal> Deals { get; set; } = new();

            public ContactMetrics? Metrics { get; set; }
        }

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

        // Cached/denormalized metrics to avoid N+1 and expensive per-row calculations during search
        public class ContactMetrics
        {
            public int ContactId { get; set; }
            public Contact? Contact { get; set; }

            public int InteractionCount { get; set; }
            public decimal DealSum { get; set; }

            // Precomputed, searchable value
            public decimal PotentialDealValueCached { get; set; }

            public DateTime CachedAsOfUtc { get; set; }
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
            public List<string> Tags { get; set; } = new();
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

        // SQLite DbContext with seed + indexes
        public class CrmDbContext : DbContext
        {
            private readonly string _databasePath;

            public DbSet<Contact> Contacts => Set<Contact>();
            public DbSet<Tag> Tags => Set<Tag>();
            public DbSet<Interaction> Interactions => Set<Interaction>();
            public DbSet<Deal> Deals => Set<Deal>();
            public DbSet<ContactMetrics> ContactMetrics => Set<ContactMetrics>();

            public CrmDbContext(string? databasePath = null)
            {
                _databasePath = databasePath ?? Path.Combine(AppContext.BaseDirectory, "crm_contacts.sqlite");

                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                if (!optionsBuilder.IsConfigured)
                {
                    optionsBuilder.UseSqlite($"Data Source={_databasePath}");
                }
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Contact>().HasKey(c => c.Id);

                modelBuilder.Entity<Tag>().HasKey(t => t.Id);
                modelBuilder.Entity<Tag>()
                    .HasOne(t => t.Contact)
                    .WithMany(c => c.Tags)
                    .HasForeignKey(t => t.ContactId)
                    .OnDelete(DeleteBehavior.Cascade);

                modelBuilder.Entity<Interaction>().HasKey(i => i.Id);
                modelBuilder.Entity<Interaction>()
                    .HasOne(i => i.Contact)
                    .WithMany(c => c.Interactions)
                    .HasForeignKey(i => i.ContactId)
                    .OnDelete(DeleteBehavior.Cascade);

                modelBuilder.Entity<Deal>().HasKey(d => d.Id);
                modelBuilder.Entity<Deal>()
                    .HasOne(d => d.Contact)
                    .WithMany(c => c.Deals)
                    .HasForeignKey(d => d.ContactId)
                    .OnDelete(DeleteBehavior.Cascade);

                modelBuilder.Entity<ContactMetrics>().HasKey(m => m.ContactId);
                modelBuilder.Entity<ContactMetrics>()
                    .HasOne(m => m.Contact)
                    .WithOne(c => c.Metrics)
                    .HasForeignKey<ContactMetrics>(m => m.ContactId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Indexes aligned with search filters + sort
                modelBuilder.Entity<Contact>()
                    .HasIndex(c => new { c.City, c.LastContactDate });

                modelBuilder.Entity<Contact>()
                    .HasIndex(c => new { c.DealStage, c.LastContactDate });

                modelBuilder.Entity<Contact>()
                    .HasIndex(c => new { c.LastContactDate, c.LastName, c.FirstName, c.Id });

                modelBuilder.Entity<Tag>()
                    .HasIndex(t => new { t.ContactId, t.Name });

                modelBuilder.Entity<ContactMetrics>()
                    .HasIndex(m => m.PotentialDealValueCached);

                modelBuilder.Entity<ContactMetrics>()
                    .HasIndex(m => m.InteractionCount);

                modelBuilder.Entity<ContactMetrics>()
                    .HasIndex(m => m.DealSum);
            }

            public async Task EnsureCreatedAndSeededAsync()
            {
                await Database.EnsureCreatedAsync().ConfigureAwait(false);

                if (!await Contacts.AsNoTracking().AnyAsync().ConfigureAwait(false))
                {
                    await GenerateMockDataAsync().ConfigureAwait(false);
                }
            }

            private async Task GenerateMockDataAsync()
            {
                const int totalContacts = 50000;

                var random = new Random(42);
                var now = DateTime.UtcNow;

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
                        LastContactDate = DateTime.SpecifyKind(DateTime.Now.AddDays(-random.Next(1, 730)), DateTimeKind.Local),
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

                    // Compute cached metrics from in-memory nav collections (no N+1)
                    var metrics = new ContactMetrics
                    {
                        ContactId = contact.Id,
                        InteractionCount = contact.Interactions.Count,
                        DealSum = contact.Deals.Sum(d => d.EstimatedValue ?? 0m),
                        PotentialDealValueCached = CalculatePotentialDealValueFromInMemory(contact, now),
                        CachedAsOfUtc = now
                    };

                    contact.Metrics = metrics;
                    contacts.Add(contact);
                }

                await Contacts.AddRangeAsync(contacts).ConfigureAwait(false);
                await SaveChangesAsync().ConfigureAwait(false);
            }

            private static decimal CalculatePotentialDealValueFromInMemory(Contact contact, DateTime nowUtc)
            {
                decimal baseValue = contact.BasePotentialValue ?? 0m;

                foreach (var interaction in contact.Interactions)
                {
                    baseValue *= GetInteractionMultiplier(interaction.Type);
                }

                baseValue *= GetStageMultiplier(contact.DealStage);

                baseValue += contact.Deals.Sum(d => d.EstimatedValue ?? 0m);

                var lastContactUtc = contact.LastContactDate.Kind == DateTimeKind.Utc
                    ? contact.LastContactDate
                    : contact.LastContactDate.ToUniversalTime();

                var daysSinceLastContact = (nowUtc - lastContactUtc).Days;
                if (daysSinceLastContact > 30)
                {
                    // Keep logic identical to legacy (decay). This is computed at seed/update time, not during search.
                    baseValue *= (decimal)Math.Pow(0.99, daysSinceLastContact - 30);
                }

                return Math.Round(baseValue, 2);
            }

            private static decimal GetInteractionMultiplier(InteractionType type)
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

            private static decimal GetStageMultiplier(DealStage? stage)
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

        // Optimized search service: DB filtering + DB sorting + DB pagination + no N+1
        public class ContactSearcher
        {
            private readonly CrmDbContext _context;

            public ContactSearcher(CrmDbContext context)
            {
                _context = context;
            }

            public async Task<SearchResult> SearchContacts(ContactSearchRequest request)
            {
                var sw = Stopwatch.StartNew();

                // Ensure DB is created and seeded during the run (fits your test expectations).
                await _context.EnsureCreatedAndSeededAsync().ConfigureAwait(false);

                var page = request.Page.GetValueOrDefault(1);
                if (page < 1) page = 1;

                var pageSize = request.PageSize.GetValueOrDefault(50);
                if (pageSize < 1) pageSize = 1;
                if (pageSize > 200) pageSize = 200;

                var tagSet = ParseTags(request.Tags);

                // Base filtered query (no materialization here)
                var baseQuery =
                    from c in _context.Contacts.AsNoTracking()
                    join m in _context.ContactMetrics.AsNoTracking() on c.Id equals m.ContactId
                    select new
                    {
                        Contact = c,
                        Metrics = m
                    };

                if (!string.IsNullOrWhiteSpace(request.City))
                {
                    var city = request.City.Trim();
                    baseQuery = baseQuery.Where(x => x.Contact.City == city);
                }

                if (request.LastContactBefore.HasValue)
                {
                    var cutoff = request.LastContactBefore.Value;
                    baseQuery = baseQuery.Where(x => x.Contact.LastContactDate < cutoff);
                }

                if (request.DealStage.HasValue)
                {
                    var stage = request.DealStage.Value;
                    baseQuery = baseQuery.Where(x => x.Contact.DealStage == stage);
                }

                if (request.MinDealValue.HasValue)
                {
                    var min = request.MinDealValue.Value;
                    baseQuery = baseQuery.Where(x => x.Metrics.PotentialDealValueCached > min);
                }

                if (tagSet.Count > 0)
                {
                    baseQuery = baseQuery.Where(x =>
                        _context.Tags.Any(t => t.ContactId == x.Contact.Id && tagSet.Contains(t.Name))
                    );
                }

                // Total count (separate query, still fast for 5k)
                var totalCount = await baseQuery.CountAsync().ConfigureAwait(false);

                // Ordering + DB pagination
                var orderedQuery = baseQuery
                    .OrderByDescending(x => x.Contact.LastContactDate)
                    .ThenBy(x => x.Contact.LastName)
                    .ThenBy(x => x.Contact.FirstName)
                    .ThenBy(x => x.Contact.Id);

                var startIndex = (page - 1) * pageSize;

                var pageRows = await orderedQuery
                    .Skip(startIndex)
                    .Take(pageSize)
                    .Select(x => new PageRow
                    {
                        Id = x.Contact.Id,
                        FirstName = x.Contact.FirstName,
                        LastName = x.Contact.LastName,
                        Email = x.Contact.Email,
                        Company = x.Contact.Company,
                        City = x.Contact.City,
                        LastContactDate = x.Contact.LastContactDate,
                        DealValue = x.Metrics.PotentialDealValueCached,
                        InteractionCount = x.Metrics.InteractionCount
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Batch load tags for only the contacts in the page (no Include, no N+1)
                var ids = pageRows.Select(r => r.Id).ToArray();

                var tagPairs = await _context.Tags.AsNoTracking()
                    .Where(t => ids.Contains(t.ContactId))
                    .Select(t => new { t.ContactId, t.Name })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var tagsByContact = tagPairs
                    .GroupBy(x => x.ContactId)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.Name).Distinct().ToList());

                var data = pageRows.Select(r => new ContactDto
                {
                    Id = r.Id,
                    FullName = r.FirstName + " " + r.LastName,
                    Email = r.Email,
                    Company = r.Company,
                    City = r.City,
                    LastContact = r.LastContactDate,
                    DealValue = r.DealValue,
                    InteractionCount = r.InteractionCount,
                    Tags = tagsByContact.TryGetValue(r.Id, out var list) ? list : new List<string>()
                }).ToList();

                sw.Stop();

                return new SearchResult
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    Data = data,
                    ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds
                };
            }

            private static HashSet<string> ParseTags(string? tags)
            {
                if (string.IsNullOrWhiteSpace(tags))
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                return tags
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            private sealed class PageRow
            {
                public int Id { get; set; }
                public string FirstName { get; set; } = string.Empty;
                public string LastName { get; set; } = string.Empty;
                public string Email { get; set; } = string.Empty;
                public string Company { get; set; } = string.Empty;
                public string City { get; set; } = string.Empty;
                public DateTime LastContactDate { get; set; }
                public decimal DealValue { get; set; }
                public int InteractionCount { get; set; }
            }
        }

        public class SearchResult
        {
            public int TotalCount { get; set; }
            public int Page { get; set; }
            public int PageSize { get; set; }
            public List<ContactDto> Data { get; set; } = new();
            public double ElapsedMilliseconds { get; set; }
        }
    }
}
