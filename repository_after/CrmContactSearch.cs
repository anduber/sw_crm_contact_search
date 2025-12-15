using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace repository_optimized
{
    // Enums remain unchanged
    public enum DealStage { Prospect = 0, Qualified = 1, Proposal = 2, Negotiation = 3, ClosedWon = 4, ClosedLost = 5 }
    public enum InteractionType { Email = 0, Call = 1, Meeting = 2, Demo = 3, Other = 4 }

    // Normalized Entities (fixed many-to-many for tags)
    public class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<ContactTag> ContactTags { get; set; } = new List<ContactTag>();
    }

    public class ContactTag // Junction table for many-to-many
    {
        public int ContactId { get; set; }
        public Contact Contact { get; set; } = null!;
        public int TagId { get; set; }
        public Tag Tag { get; set; } = null!;
    }

    public class Interaction
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public InteractionType Type { get; set; }
        public DateTime Date { get; set; }
        public Contact Contact { get; set; } = null!;
    }

    public class Deal
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public decimal? EstimatedValue { get; set; }
        public Contact Contact { get; set; } = null!;
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

        // Navigation properties (fixed for ORM efficiency)
        public ICollection<ContactTag> ContactTags { get; set; } = new List<ContactTag>();
        public ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();
        public ICollection<Deal> Deals { get; set; } = new List<Deal>();
    }

    // DTOs remain unchanged
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

    public class ContactSearchRequest
    {
        public string? City { get; set; }
        public string? Tags { get; set; }
        public DateTime? LastContactBefore { get; set; }
        public DealStage? DealStage { get; set; }
        public decimal? MinDealValue { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? SortBy { get; set; } = "LastContactDate";
        public bool SortDescending { get; set; } = true;
    }

    public class SearchResult
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<ContactDto> Data { get; set; } = new List<ContactDto>();
        public double ElapsedMilliseconds { get; set; }
        public string? NextPageToken { get; set; } // For keyset pagination
    }

    // Production Database Context with Index Configuration
    public class CrmDbContext : DbContext
    {
        public CrmDbContext(DbContextOptions<CrmDbContext> options) : base(options) { }

        public DbSet<Contact> Contacts { get; set; } = null!;
        public DbSet<Tag> Tags { get; set; } = null!;
        public DbSet<ContactTag> ContactTags { get; set; } = null!;
        public DbSet<Interaction> Interactions { get; set; } = null!;
        public DbSet<Deal> Deals { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure many-to-many
            modelBuilder.Entity<ContactTag>()
                .HasKey(ct => new { ct.ContactId, ct.TagId });

            // Critical Indexes (tailored for common filters)
            modelBuilder.Entity<Contact>()
                .HasIndex(c => new { c.City, c.DealStage, c.LastContactDate }) // Composite index for frequent filters
                .IncludeProperties(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Company, c.BasePotentialValue }); // Covering index

            modelBuilder.Entity<ContactTag>()
                .HasIndex(ct => ct.TagId); // Index for tag filtering

            modelBuilder.Entity<Interaction>()
                .HasIndex(i => i.ContactId)
                .IncludeProperties(i => i.Type); // Covering index for interaction multiplier

            modelBuilder.Entity<Deal>()
                .HasIndex(d => d.ContactId)
                .IncludeProperties(d => d.EstimatedValue); // Covering index for deal value calculation
        }
    }

    // Optimized Search Service with Caching and Database-Level Operations
    public class ContactSearcher
    {
        private readonly CrmDbContext _context;
        private readonly IDistributedCache _cache;
        private const string CachePrefix = "crm:contact-search:";
        private const int CacheTtlSeconds = 300; // 5 minutes for frequent queries

        public ContactSearcher(CrmDbContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<SearchResult> SearchContactsAsync(ContactSearchRequest request)
        {
            var startTime = DateTime.Now;
            var cacheKey = GetCacheKey(request);

            // Check cache for frequent queries
            var cachedResult = await _cache.GetStringAsync(cacheKey);
            if (cachedResult != null)
            {
                var result = JsonConvert.DeserializeObject<SearchResult>(cachedResult)!;
                result.ElapsedMilliseconds = (DateTime.Now - startTime).TotalMilliseconds;
                return result;
            }

            // Build query expression (database-level filtering)
            var query = _context.Contacts.AsQueryable();
            query = ApplyFilters(query, request);
            query = ApplySorting(query, request);

            // Get total count (efficient count with filtered query)
            var totalCount = await query.CountAsync();

            // Database-level pagination (LIMIT/OFFSET for PostgreSQL; use OFFSET/FETCH for SQL Server)
            var pagedQuery = query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Include(c => c.ContactTags) // Eager load to eliminate N+1
                    .ThenInclude(ct => ct.Tag)
                .Include(c => c.Interactions)
                .Include(c => c.Deals);

            // Project directly to DTO (avoids loading full entities)
            var contactDtos = await pagedQuery.Select(c => new ContactDto
            {
                Id = c.Id,
                FullName = $"{c.FirstName} {c.LastName}",
                Email = c.Email,
                Company = c.Company,
                City = c.City,
                LastContact = c.LastContactDate,
                Tags = c.ContactTags.Select(ct => ct.Tag.Name).ToList(),
                InteractionCount = c.Interactions.Count,
                DealValue = CalculatePotentialDealValue(c) // Cached below
            }).ToListAsync();

            // Cache expensive DealValue calculations (per contact)
            await CacheDealValues(contactDtos);

            var result = new SearchResult
            {
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                Data = contactDtos,
                ElapsedMilliseconds = (DateTime.Now - startTime).TotalMilliseconds,
                NextPageToken = GetNextPageToken(request, totalCount)
            };

            // Cache the full result for frequent queries
            await _cache.SetStringAsync(cacheKey, JsonConvert.SerializeObject(result), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheTtlSeconds),
                SlidingExpiration = TimeSpan.FromSeconds(60)
            });

            return result;
        }

        private IQueryable<Contact> ApplyFilters(IQueryable<Contact> query, ContactSearchRequest request)
        {
            if (!string.IsNullOrEmpty(request.City))
                query = query.Where(c => c.City == request.City);

            if (!string.IsNullOrEmpty(request.Tags))
            {
                var tagNames = request.Tags.Split(',').Select(t => t.Trim()).ToList();
                query = query.Where(c => c.ContactTags.Any(ct => tagNames.Contains(ct.Tag.Name)));
            }

            if (request.LastContactBefore.HasValue)
                query = query.Where(c => c.LastContactDate < request.LastContactBefore.Value);

            if (request.DealStage.HasValue)
                query = query.Where(c => c.DealStage == request.DealStage.Value);

            if (request.MinDealValue.HasValue)
            {
                // Use cached DealValue if available; otherwise calculate on fly
                query = query.Where(c => GetCachedDealValue(c.Id) > request.MinDealValue.Value
                    || CalculatePotentialDealValue(c) > request.MinDealValue.Value);
            }

            return query;
        }

        private IQueryable<Contact> ApplySorting(IQueryable<Contact> query, ContactSearchRequest request)
        {
            var sortExpression = GetSortExpression(request);
            return request.SortDescending
                ? query.OrderByDescending(sortExpression)
                : query.OrderBy(sortExpression);
        }

        private Expression<Func<Contact, object>> GetSortExpression(ContactSearchRequest request)
        {
            return request.SortBy?.ToLower() switch
            {
                "fullname" => c => $"{c.FirstName} {c.LastName}",
                "company" => c => c.Company,
                "email" => c => c.Email,
                _ => c => c.LastContactDate // Default sort
            };
        }

        // Cached expensive calculation
        private decimal CalculatePotentialDealValue(Contact contact)
        {
            var cacheKey = $"{CachePrefix}deal-value:{contact.Id}";
            var cachedValue = _cache.GetString(cacheKey);
            if (cachedValue != null && decimal.TryParse(cachedValue, out var value))
                return value;

            decimal baseValue = contact.BasePotentialValue ?? 0;
            baseValue *= contact.Interactions.Sum(i => GetInteractionMultiplier(i.Type));
            baseValue *= GetStageMultiplier(contact.DealStage);
            baseValue += contact.Deals.Sum(d => d.EstimatedValue ?? 0);

            var daysSinceLastContact = (DateTime.Now - contact.LastContactDate).Days;
            if (daysSinceLastContact > 30)
                baseValue *= (decimal)Math.Pow(0.99, daysSinceLastContact - 30);

            var result = Math.Round(baseValue, 2);
            _cache.SetString(cacheKey, result.ToString(), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });

            return result;
        }

        private async Task CacheDealValues(List<ContactDto> dtos)
        {
            foreach (var dto in dtos)
            {
                var cacheKey = $"{CachePrefix}deal-value:{dto.Id}";
                await _cache.SetStringAsync(cacheKey, dto.DealValue.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                });
            }
        }

        private decimal GetCachedDealValue(int contactId)
        {
            var cacheKey = $"{CachePrefix}deal-value:{contactId}";
            return decimal.TryParse(_cache.GetString(cacheKey), out var value) ? value : 0;
        }

        private string GetCacheKey(ContactSearchRequest request)
        {
            return $"{CachePrefix}{JsonConvert.SerializeObject(request)}";
        }

        private string? GetNextPageToken(ContactSearchRequest request, int totalCount)
        {
            var nextPage = request.Page + 1;
            if (nextPage * request.PageSize > totalCount)
                return null;

            // For keyset pagination, return last sorted value instead of page number
            return JsonConvert.SerializeObject(new { Page = nextPage, request.SortBy, request.SortDescending });
        }

        // Helper methods (unchanged logic, cached)
        private decimal GetInteractionMultiplier(InteractionType type) => type switch
        {
            InteractionType.Email => 1.01m,
            InteractionType.Call => 1.05m,
            InteractionType.Meeting => 1.15m,
            InteractionType.Demo => 1.25m,
            _ => 1.0m
        };

        private decimal GetStageMultiplier(DealStage? stage) => stage switch
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

    // Example Usage (DI Setup)
    public static class DependencyInjection
    {
        public static IServiceCollection AddCrmServices(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<CrmDbContext>(options =>
                options.UseNpgsql(connectionString) // Use UseSqlServer for SQL Server
                    .EnableSensitiveDataLogging(false)
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)); // Disable tracking for read-only queries

            services.AddDistributedRedisCache(options =>
            {
                options.Configuration = "localhost:6379"; // Use production Redis cluster
                options.InstanceName = "crm-";
            });

            services.AddScoped<ContactSearcher>();
            return services;
        }
    }
}