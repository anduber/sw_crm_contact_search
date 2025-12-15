# Trajectory (Thinking Process for Refactoring)

1.  I audited the original code. It loaded full tables into memory for filtering and sorting, applied pagination too late, repeatedly recalculated deal values, and used N+1 queries. This design would not scale.

2.  I defined a performance contract first. Filtering and ordering must stay in the database. Ordering must be stable. We would use keyset pagination, forbid N+1 patterns, and avoid per-request aggregations.

3.  I reworked the data model for efficiency. A new ContactMetrics table stores precomputed values like cached deal value and interaction count. The search path no longer joins the heavy deals or interactions tables.

4.  I rebuilt the search as a projection-first pipeline. It now projects only needed fields into a lightweight shape, preventing EF from materializing large entity graphs.

5.  All filters moved to SQL. City, date, deal stage, and minimum value became simple server-side predicates that work with indexes.

6.  Tag filtering now uses an EXISTS subquery. This avoids eager loading and prevents cartesian explosions in the result set.

7.  I implemented stable ordering and keyset pagination. Results order by last contact date, last name, first name, and ID. A cursor-based seek predicate makes deep paging fast without offset scans.

8.  I eliminated N+1 queries for enrichment. The code fetches a page of contacts, then loads all their tags in one single follow-up query before mapping them in memory.

9.  I added normalization for case-insensitive tag searches. A NormalizedName column on the Tag table, plus normalized request input, ensures consistent behavior.

10. The solution is built around verifiable signals. It uses two queries per request, never touches the heavy tables during search, maintains stable ordering, and shows measurable performance gains.