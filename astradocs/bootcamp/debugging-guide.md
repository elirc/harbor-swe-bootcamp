# Debugging a feature without guessing

Use this alongside the [journal](journal.md). The journal records what actually
happened during implementation; this guide turns those observations into a method
you can reuse.

## Begin with an observable disagreement

Write two short statements:

```text
Expected: what the user contract says should happen.
Observed: what the HTTP response, database read, or test failure actually showed.
```

Avoid beginning with a proposed fix. "The query is broken" is a theory.
"The response returned two contacts and the expected fixture contains one" is an
observation. Keeping them separate makes the next experiment easier to choose.

## Follow the evidence to the right layer

| Observation | First place to inspect | A useful question |
| --- | --- | --- |
| Project does not compile | Response/request type and compiler location | Did I assume a property that does not exist? |
| HTTP 400 before the action runs | Request DTO and model validation | Is the input malformed or intentionally rejected? |
| HTTP 403 | Role requirement and authenticated identity | Does this caller have permission to perform the action? |
| HTTP 404 for a known ID | Scoped lookup and caller workspace/owner | Is the ID accessible to this caller? |
| Right rows, wrong count | Where counting happens relative to filtering/paging | Which population does the count describe? |
| Duplicate conversation summaries | Joins or relationship projection | Did I count relationship rows instead of parents? |
| Runtime SQL translation failure | Last query composition before execution | Did a .NET-only method enter the SQL expression? |
| HTTP 409 | Version checks, uniqueness, transaction boundary | Was a conflicting write correctly refused? |
| Fresh-model tests pass, app upgrade fails | Migration and model snapshot | Does the migration create every required column? |

The table is a starting point, not a diagnosis. Confirm the cause with a narrow
experiment before changing code.

## Worked incident: the missing header

One new contact test successfully read the expected page and total count, then
failed while accessing `X-PageSize`. The established API emits `X-Page-Size`.

Reasoning sequence:

1. The action returned successfully, so the failure was not a rejected request.
2. Row and count assertions had already passed, so the immediate evidence did not
   suggest a membership or pagination algorithm defect.
3. The stack pointed to the header lookup in the test.
4. Searching `Paging.cs` showed the existing header constant.
5. The assertion was corrected to the actual documented contract.

The first run had 77 passing checks and this one failure. The later combined
junior run passed 129 checks. Both facts belong in the journal; keeping only the
green result would hide a useful lesson about mistaken expectations.

Practice: explain why changing the production header to satisfy this assertion
would have been a compatibility change, not merely a test fix.

## Worked incident: the guessed response shape

While adding directory tests, an initial draft referred to `teammate.Teammate.Id`
and `inbox.SlaMinutes`. The actual records expose `teammate.Id` and
`inbox.FirstResponseSlaMinutes`. Reading `Responses.cs` corrected the draft before
the subsequent compile.

The reusable habit is simple: inspect the helper's return type, then inspect that
type's declaration. A similar-looking API in another project is not evidence for
this project's shape. Strong typing helps find this error early, but only if the
test or feature is actually compiled.

## Worked design check: whitespace has a definition

The contact presence contract treats tabs and Unicode whitespace as blank. A
default SQLite `trim` does not automatically mean the same thing as .NET
whitespace handling. The implementation passes an explicit whitespace character
set to the mapped SQL trim function, and tests cover tabs and Unicode spaces.

This was not solved by assuming that similar method names imply identical
semantics. The contract defined the behavior; the database tests checked the
translation. Read [Lesson 01](lessons/01-contact-queries.md), then predict which of
`null`, `""`, `"\t"`, and `" x "` counts as present.

## Make one experiment discriminate between two explanations

### Worked incident: first page passed, second page returned everything

The new timeline test requested page size two. Page one was correct, but adding
`page=2` returned all four entries. The action parameter was named `page` and its
type also contained a `Page` property. ASP.NET model binding treated the query's
`page` key as the complex parameter prefix, so the unprefixed paging fields did
not bind as intended and defaults were used.

Renaming the complex parameter to `paging`, following the established controller
convention, removed the collision. The same parameter name was corrected across
the six new controllers that used it. The subsequent 777-test midlevel suite
passed, and the final 857-test suite passed too. The journal preserves the
original failure separately from those later verification results.

The useful test distinction is **page one versus page two with the same page
size**. Merely checking a count header or the default page would not have exposed
this bug. Trace input binding before assuming the SQL `Skip` calculation failed.

### Choose a discriminating fixture

Suppose a tag filter returns a conversation twice. Two possibilities are a
duplicate stored conversation or a join producing one row per matching tag.
Inspect the conversation table by its ID, then arrange one conversation with two
tags. If the stored parent is unique but the result duplicates it, investigate the
relationship query. Adding random conditions until the test passes would not
explain the cause.

Suppose a draft update gets 409. First compare the submitted version with a fresh
GET. If they differ, the conflict may be correct behavior. Do not remove the
version check merely to make the response become 200.

## Run the smallest useful group, then the needed regressions

From the repository root, examples are:

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~ContactLearningTests'
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~PersonalWorkLearningTests|FullyQualifiedName~PersonalWorkPersistenceTests'
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~TriageLearningTests|FullyQualifiedName~AssignmentRulesTests'
```

If this machine has not put dotnet on PATH, the installed SDK used for this work
is `C:/Users/E/.dotnet/dotnet.exe`; invoke it with PowerShell's `&` operator. The
[run-and-observe guide](../10-run-and-observe.md) provides broader setup context.

A filter can accidentally select zero tests. Always read the selected total.
After a shared assignment or SLA workflow changes, include its existing
regressions. After a model changes, include migrations. A successful targeted
test is evidence for its assertions, not proof of the entire application.

## Read failures before rerunning

Capture the failing test name, expected and actual values, first relevant source
line, and the exact command. If nothing changed and the failure is deterministic,
another identical run is unlikely to teach anything new. Read or change the
smallest relevant piece first.

For long runs, use a TRX report when useful. The current full-regression command
records its report under `.artifacts/bootcamp`; that generated directory is
ignored by Git. A report from an older compiled snapshot must not be presented
as verification of code edited afterward.

## Finish with a teach-back

Write: "The failure occurred because ___. The change fixes it by ___. The test
would catch ___, but it does not prove ___." Then explain the incident to a
teammate using one concrete request and one concrete result. That final sentence
about limitations is part of engineering judgment, not a reason to distrust every
test.
