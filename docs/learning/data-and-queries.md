# Data and queries: learn to see the work

Goal: explain both a query's result and where its work happens. Start with
[ConversationQueries.cs](../../src/Harbor.Api/Infrastructure/ConversationQueries.cs)
and [Paging.cs](../../src/Harbor.Api/Infrastructure/Paging.cs).

## Compose before materializing

An `IQueryable<Conversation>` describes database work. `Where`, `OrderBy`, and
`Take` add to that description. `ToListAsync` executes it and brings rows into
application memory. Moving `ToListAsync` earlier can preserve the visible result
while changing a bounded request into a full-table load.

Harbor's segment filter composes a contact-ID subquery. It does not fetch all
matching IDs and paste them into an in-memory list. Read
[SegmentSqlTranslationTests.cs](../../tests/Harbor.Tests/Integration/SegmentSqlTranslationTests.cs)
to see why result assertions and SQL assertions answer different questions.

Exercise: inside a test's `Factory.WithDb` scope, inspect a simple query:

```csharp
var query = db.Conversations
    .Where(c => c.WorkspaceId == workspaceId)
    .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
    .Take(2);
var sql = query.ToQueryString();
```

Use the debugger to read `sql`. Identify the workspace predicate, ordering, and
limit. Do not assert the entire SQL string: whitespace, aliases, and parameter
names are usually irrelevant to the behavior you want to protect.

## Membership, order, and page are different operations

For a queue of 500 matching conversations, page size 2 should return two rows
while `X-Total-Count` remains 500. Filtering after paging can return too few rows
and the wrong total. Counting after paging answers how many rows are on that page.

The overview counts the same filtered population without applying paging. Its
`open + snoozed + closed` equals `total` for valid conversation states.
`unassigned` overlaps these groups; do not add it to the total. A filter of
`state=Open` means the overview's closed count is zero. This endpoint is not a
set of facets that each ignore their own filter.

Exercise: use a fixture containing open, snoozed, closed, team-assigned, and
unassigned conversations. Write expected counts on paper for three combined
filters before running them through both API paths.

## Timestamps need exact semantics

The new bounds describe creation time, not last activity:

```text
createdFrom <= CreatedAt < createdBefore
```

Adjacent windows `[Jan 1, Jan 2)` and `[Jan 2, Jan 3)` neither overlap nor leave
a gap. Avoid inventing an end-of-day timestamp ending in `.999`; the database
can represent greater precision. Always include a timezone in client examples.

Harbor converts `DateTimeOffset` to UTC ticks for SQLite. Two different offsets
representing the same instant should compare identically. Read
[UtcDateTimeOffsetConverter.cs](../../src/Harbor.Infrastructure/UtcDateTimeOffsetConverter.cs)
and test a boundary using `Z` and `-05:00` representations.

## A total order makes static pages reproducible

Multiple rows can have the same timestamp. Harbor now appends `Id` ascending to
each supported conversation ordering. Database pagination needs unique ordering;
Microsoft's [EF pagination guidance](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
explains the underlying requirement and the offset/keyset alternatives.

This does not freeze a changing queue. If a new row enters before your current
offset, the next page can repeat a row; if a row leaves, it can skip one. A cursor
and a snapshot address different parts of this problem. Describe which behavior
your user actually needs before choosing either.

Exercise: fetch page 1, insert a conversation with a newer activity time, then
fetch page 2. Explain any overlap using the ordering, without calling it a tie bug.

## Bound arithmetic as well as results

`(page - 1) * size` can overflow a 32-bit integer even when `page` and `size`
individually bind successfully. Harbor computes the product as `long` and returns
an empty page when the offset is beyond the count. Only a representable, useful
offset reaches `Skip(int)`.

The cast must happen **before** multiplication. `(long)((page - 1) * size)` only
widens a result that may already be wrong. See [the debugging lab](debugging-labs.md).

## Measure before indexing

For a controlled local experiment, use synthetic data with multiple tenants and
realistic variation in states and timestamps. Record row counts, filters,
generated SQL, warm-up strategy, repeated timings, and result size. Compare a
candidate index using SQLite's query plan, then measure again. Count queries and
substring searches may remain expensive even when the response contains few rows.

`AsNoTracking` avoids retaining read entities for later updates; projection can
reduce selected columns. Neither is a substitute for measuring the actual query.
Microsoft's [efficient querying guide](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
provides background on these techniques.

Exit criteria: show one measured improvement, one preserved behavior, and one
limitation of the experiment. Next: [worked feature](worked-feature.md).
