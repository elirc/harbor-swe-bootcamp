# Worked feature: a filtered conversation queue

This feature is implemented. Use it as a code-reading and extension exercise.
The related [ADR](../adr/0010-shared-conversation-queries.md) records the design.

## Start with a user problem

An agent needs to inspect older conversations and see how much work matches the
current filters. The previous list sorted only by recent activity. A client
could not select a creation window or get counts for the complete filtered queue
without fetching pages and doing extra work.

Acceptance criteria:

- `createdFrom` is inclusive and `createdBefore` exclusive; omitted bounds are open.
- Equal or reversed bounds produce a 400 validation problem.
- `sort` accepts `RecentActivity`, `OldestCreated`, or `NewestCreated`; the default remains recent activity.
- All sort modes break timestamp ties by ID ascending.
- `GET /api/workspaces/{id}/conversations/overview` returns total, state, and unassigned counts for all matches.
- List and overview apply the same existing filters, including segments and SLA breaches.
- Overview counts ignore page and page size; empty results return five zeros.
- Both reads enforce the existing tenant boundary and segment error contract.
- Extreme page numbers return an empty array with truthful metadata.

## Read the implementation in four small pieces

**Contract:** [Requests.cs](../../src/Harbor.Api/Contracts/Requests.cs) adds date
bounds and a validated sort enum. Relational validation belongs on the filter
because `from < before` depends on two properties. No database column changes
are needed: the timestamps already exist.

**Query membership:** [ConversationQueries.cs](../../src/Harbor.Api/Infrastructure/ConversationQueries.cs)
extracts existing list conditions. The query starts scoped to a workspace and
uses no tracking. Time is supplied once by the caller for SLA evaluation, so a
single request does not take a fresh clock reading per condition. The optional
segment lookup returns null when unavailable; the controller decides the HTTP
response. This small convention avoids a new exception or result-type framework.

**Shared segment behavior:** [SegmentQueries.cs](../../src/Harbor.Infrastructure/SegmentQueries.cs)
owns rule deserialization and contact membership. A conversation query no longer
calls static helpers on another controller. Segment endpoints and SQL-translation
tests use the same infrastructure helper.

**Read execution:** [ConversationsController.cs](../../src/Harbor.Api/Controllers/ConversationsController.cs)
uses the shared query for two purposes. The list adds order, related tags, and
paging. The overview groups by a constant and projects five conditional counts
into one response object. An empty grouping produces no row, so the endpoint
explicitly returns zeros. There is no need to materialize conversation entities
to count them. Cancellation reaches the database calls on these two paths.

## Before and after: the important distinction

Previously, filter membership was embedded in `List`. Adding overview by copying
that block would create two places to update every time a filter changed.
Now both actions call `BuildAsync`, then choose their own result shape. This is
a responsibility extracted because it has two consumers, not a blanket rule
that every controller needs repositories, command buses, and service interfaces.

The response array, existing paging headers, existing filters, and default order
remain compatible. Ties now have an explicit order. `unassigned=false` and
`slaBreached=false` still mean no restriction because that was the existing
contract; changing them into negative filters would be a separate API decision.

## Read the tests as a specification

[ConversationQueueTests.cs](../../tests/Harbor.Tests/Integration/ConversationQueueTests.cs)
uses deliberately different conversations and a matching foreign-tenant row.
The table of filters supplies independent expected IDs. Each case checks the
list and overview against those expectations, rather than only comparing the
two endpoints to each other: two equally wrong implementations could agree.

Other cases cover tied timestamps across pages, newest ordering, invalid sort
values, invalid dates, tenant authorization, and foreign/missing segments.
[PagingOverflowTests.cs](../../tests/Harbor.Tests/Unit/PagingOverflowTests.cs)
protects the in-memory paging path as well as the HTTP tests' database path.

```powershell
dotnet test --filter "FullyQualifiedName~ConversationQueueTests|FullyQualifiedName~PagingOverflowTests|FullyQualifiedName~SegmentSqlTranslationTests"
```

## Extend it yourself

Exercise A, 30–45 minutes: add combined-filter rows to the test table, including
an impossible assignment/state combination and a creation window with no matches.
Explain the expected set before running the test.

Exercise B, 60–90 minutes: propose and implement `awaitingFirstResponse=true` as
an optional filter on a practice branch. First define whether closed conversations
with no response count, and whether `false` restricts results. Add the rule once,
then test both reads, document its semantics, and check backward compatibility.

Exercise C, 90 minutes: inspect the overview's generated SQL using EF logging or
a command interceptor. Establish that conversations are counted in SQL and that
no message bodies are selected. Avoid a stopwatch assertion in a unit test.

## Limits you should explain in review

Offset paging is not a stable snapshot under concurrent writes. List count and
page are separate reads; a later overview request may observe different data.
Counts and substring search can scan many rows despite bounded output. The new
query component serves conversation list/overview; the older reporting endpoints
still have their own filter contract and materialization behavior.

No migration or index was added. A useful next performance change would begin
with a measured workload. Next: [testing workshop](testing-workshop.md).
