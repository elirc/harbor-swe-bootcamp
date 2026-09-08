# 08: Reports begin with a population

Stories: M19-M21. Code is implemented; consult the [tracker](../progress.md) for executed verification. This lesson explains the report changes without assuming that a successful compilation proves the numbers.

## The user problem

A support lead wants to know how old today's unfinished work is, which teams own work, and how to download the same volume numbers shown by the API. These sound like three formatting tasks. The difficult part is defining exactly which conversations each number describes.

A **cohort** is the set of conversations admitted by the report filters. In Harbor, the date filters select conversations by their creation time. A conversation created on Monday and first resolved on Thursday belongs to Monday's creation cohort; its resolution can appear in Thursday's volume bucket. Filtering the cohort to Monday does not move the resolution to Monday or erase it.

Read [ReportQueries.cs](../../../src/Harbor.Api/Infrastructure/ReportQueries.cs) first. Say each predicate aloud: this workspace, created at or after From, created before To, then the optional ownership/priority/tag conditions. Every additional filter narrows that population. Report filters are not the conversation queue filter contract.

## Explain it with five cards

Freeze the clock at Thursday 12:00 UTC. Draw five conversation cards:

| Card | Created | Current state | Assigned team |
| --- | --- | --- | --- |
| A | Thursday 10:00 | Open | Billing |
| B | Wednesday 12:00 | Snoozed | Billing |
| C | Monday 12:00 | Open | Technical |
| D | Sunday 12:00 | Closed | Technical |
| E | Thursday 13:00 | Open | None |

The backlog contains A, B, C, E. A and E are under 24 hours; B is in 24-72 hours; C is at least 72 hours. Exactly 24 hours belongs to the middle bucket. Exactly 72 hours belongs to the oldest bucket. A future timestamp remains in the youngest bucket rather than silently disappearing. D is excluded because backlog is unfinished work, even though it is old.

The invariant is `Total = Under24Hours + From24To72Hours + AtLeast72Hours`. Also check `Total = Open + Snoozed`. These two independently explain the same population. They are useful cross-checks when a dashboard looks plausible but wrong.

Now explain it another way: the query moves two timestamp fences backward from one captured clock value. It compares stored creation timestamps with those fences. It does not call the clock once per row. Every card is judged against the same instant.

## Follow one request

Read [ReportInsightsController.cs](../../../src/Harbor.Api/Controllers/ReportInsightsController.cs), starting at `BacklogAge`.

1. Binding creates the report filter. An invalid new-route date range returns 400.
2. Capture the evaluation time once through `TimeProvider`.
3. Build the workspace-scoped cohort with `ReportQueries.Build`.
4. Restrict it to Open or Snoozed rows.
5. Group the remaining rows into one aggregate and count each condition in SQL.
6. If the population is empty, return a response containing zeros and the evaluation time.

The clock is injected so a test can exercise an exact boundary without racing real time. SQL conditional counts avoid downloading every conversation just to increment counters. Neither choice changes the product definition; both help implement that definition faithfully.

## Why a team's roster is not its workload

A teammate can belong to Billing and Technical. That does not mean every conversation assigned to that person should count for both teams. M20 counts the conversation's explicit `AssignedTeamId`. Roster membership answers who belongs to a team; direct ownership answers where the work belongs.

The implementation pages the team directory first. It then aggregates work for those team IDs and counts their roster members separately. A team with no matching conversations still appears with zero work. If the query began from conversations, that team would disappear and pagination totals would describe working teams rather than directory teams.

Explain the flow using two dictionaries: one keyed by team ID containing work counts, another keyed by team ID containing roster counts. Each directory row looks up both. Missing dictionary entries become zero. There is no loop issuing one SQL query per team.

The reads use a transaction so the directory page, workload counts, and membership counts describe one database view. This is a read transaction; it does not save a report or mutate assignment rotation.

## CSV is another representation of the same calculation

Read [VolumeReports.cs](../../../src/Harbor.Api/Infrastructure/VolumeReports.cs), then compare the JSON volume action in [ReportsController.cs](../../../src/Harbor.Api/Controllers/ReportsController.cs) with `VolumeCsv`.

Both routes select the same creation cohort and pass it to `VolumeReports.Build`. That builder produces the bucket boundaries, Started and Closed values, totals, and zero-filled gaps. CSV serialization consumes those already calculated points. If CSV calculated dates separately, fixing the JSON report could leave downloads wrong.

Day and hour boundaries are UTC. Weeks start on Monday. The final range can extend beyond a cohort's creation filter because first resolution is a later event. The builder rejects more than 1,000 buckets and a final boundary beyond the timestamp range. A rejected report returns a problem response before producing a file.

The CSV contains a fixed header, UTC round-trip timestamps, invariant integer formatting, CRLF line endings, and UTF-8 bytes without a BOM. These columns contain generated timestamps and numbers, so this serializer does not currently handle arbitrary user text. Adding a Title column would require a deliberate CSV escaping design and spreadsheet behavior tests. Do not assume the present serializer is a general export library.

## Read the evidence

Open [ReportLearningTests.cs](../../../tests/Harbor.Tests/Integration/ReportLearningTests.cs).

1. Find the frozen clock and the exact 24/72-hour examples. Predict the counters before reading the assertions.
2. Find the team with no work and the teammate in multiple teams. Explain which accidental implementation each fixture defeats.
3. Find the CSV/JSON comparison. Check that it compares individual points, not only that both responses are successful.
4. Find the empty report. A header-only file is a valid representation of no rows.

Run this group from the repository root when your SDK is available:

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter FullyQualifiedName~ReportLearningTests
```

An assembly loading failure before a test body runs does not verify or falsify the reporting rules. Record it separately, as the [journal](../journal.md) does for the Windows Application Control failure encountered in this session.

## Practice and teach-back

Spend ten minutes drawing a conversation created before From but resolved inside the requested dates. Should the volume report count it? No: it was not admitted to the creation cohort. A resolution-event report would be a different contract.

Next, propose an OverSevenDays backlog counter. Write its relationship to the existing oldest bucket before editing code. Decide whether it is a subset or a replacement. Add a boundary example with exactly seven days. A reviewer should be able to understand the semantics from your example without reading the SQL.

Tomorrow, explain these three distinctions from memory: cohort versus event timestamp; roster versus direct ownership; calculation versus representation. Then revisit the code and find one line supporting each explanation. That ability to connect a product definition to a query is the skill this lesson is building.
