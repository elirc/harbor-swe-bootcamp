# Testing workshop: prove the behavior that matters

Goal: choose a test by the defect it should catch. Prerequisite: the
[first session](first-90-minutes.md). Reference: [test harness](../testing.md).

## Choose the smallest useful boundary

| Question | Best starting point | What it does not establish |
| --- | --- | --- |
| Does closing preserve the first resolution time? | Unit test on `Conversation` | HTTP binding or persistence |
| Does a created-date bound work with SQLite? | Integration test with real SQLite | Another database provider's behavior |
| Does the API reject a foreign workspace? | HTTP integration test | Proxy/network configuration |
| Does segment filtering remain in SQL? | Query translation assertion | Performance at production volume |
| Can a migration build the schema? | Migration replay test | Every possible upgrade of existing data |
| Does a webhook retry after a failure? | Dispatcher test with fake sender | Real external receiver availability |

Harbor uses `WebApplicationFactory` for HTTP integration tests. Microsoft's
[integration testing guide](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
explains the host substitution approach. Read the local factory to learn its
specific choices: one SQLite connection per test class and a fake webhook sender.

## Workshop 1: make an assertion earn its place

Open the queue test's `Filters` table. For `q=ALPHA`, the expected IDs include a
subject match and a reply match, but exclude a note-only match. Explain which
incorrect implementation each fixture row would catch.

Add an unrelated conversation containing no search term. If removing that row
would make a broken “return everything” implementation pass, it has a purpose.
Prefer fixtures with clearly different jobs over many nearly identical rows.

Mutation exercise: temporarily remove `m.Kind == MessageKind.Reply` from the
query on a practice branch. Run the focused tests and identify the failure.
Restore the condition before continuing. This verifies that the privacy-related
search behavior is actually protected.

## Workshop 2: boundary values

For a date window, test before, exactly at, inside, exactly at the upper bound,
and after. Add a different timezone offset representing the same instant.
For pagination, test empty results, a short last page, negative inputs, a huge
page, malformed values, and tied ordering keys.

Write the expected behavior first. `page=-1` is clamped by the current contract;
`page=banana` is a binding error. Both are unusual inputs, but they follow
different paths and should not share a guessed expected status.

## Workshop 3: avoid false confidence from parity

If list and overview both forget `workspaceId`, they might agree while leaking
another tenant's data. Compare both to an independent expected set. Add a foreign
workspace containing a row that otherwise matches the filter. Check totals as
well as returned IDs: aggregate data can leak without returning the foreign row.

Likewise, a round-trip test can miss a symmetric defect if both serialization
and parsing make the same wrong assumption. Use a fixed known example when a
protocol's exact behavior matters, such as webhook signing.

## Workshop 4: time without sleeps

Domain methods take `now`, allowing tests to choose exact times. The integration
harness also lets fixtures pin timestamps directly. Prefer exact values around
a deadline over sleeping until a real clock happens to cross it.

Exercise: design a test for “at the SLA deadline” versus “one tick later.” Explain
why a test that waits 100 milliseconds cannot reliably establish a one-tick
boundary. Then explain why exposing a clock seam in more application paths
could simplify tests without changing business behavior.

## Workshop 5: know what your database setup skips

`HarborApiFactory` uses `EnsureCreated`, building schema from today's EF model.
Development startup uses migrations. A model can pass the HTTP suite while
missing a migration required by an existing database. Read `MigrationDriftTests`
and explain why the separate guard exists.

A clean-database migration test still differs from upgrading a populated older
database. For a schema-changing capstone, add an upgrade fixture with meaningful
old data and assert what survives. Do not generate a migration for API-only
filter fields; there is no persistence change to apply.

## Finish with an evidence statement

Use this pattern: “Given [fixture], when [operation], assert [observable result].
This catches [defect]. It does not prove [limit].”

Run focused tests while iterating, then the full suite for the completed change:

```powershell
dotnet test --filter "FullyQualifiedName~ConversationQueueTests"
dotnet test
```

Exit criteria: you can delete an assertion only after explaining which protection
would be lost. Next: [debugging labs](debugging-labs.md).
