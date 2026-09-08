# 11: Make one tiny, understandable contribution

**Remember today:** a test starts with an expectation you can explain.

This is an exercise for you to perform, not an unfinished application feature.
You will add one filter case to an existing test table. You do not need a running
Development server for the tests.

## First, understand the miniature world

Open [ConversationQueueTests.cs](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs).
Find `SeedQueueAsync`, then the `Filters` table. The fixture creates these four
conversations in the workspace under test:

| Fixture index | State | Assignment | Billing tag? |
| --- | --- | --- | --- |
| 0 | Open | Unassigned | Yes |
| 1 | Snoozed | Teammate | No |
| 2 | Closed | Team | Yes |
| 3 | Open | Unassigned | No |

It also creates a matching conversation in another workspace to check isolation.
The numbers 0–3 are positions in the fixture's `Ids` array, not database IDs.

## Predict the result before editing

What should `state=Closed&tag=billing` select? Point to the row before looking
at the answer. It must satisfy both conditions.

<details>
<summary>Check the expected row</summary>

Index 2 is Closed and has the billing tag. Index 0 has the tag but is Open,
so it must not appear.

</details>

## Add one line

Inside the existing `Filters` table, add this case if it is not already present:

```csharp
{ "state=Closed&tag=billing", [2] },
```

The string describes the API query. `[2]` is the expected fixture-index array.
The existing theory takes the case through both the conversation list and the
overview and compares them with the independent expected result.

Run just that theory's cases:

```powershell
dotnet test --filter "FullyQualifiedName~ListAndOverview_ShareFilters_AndCountTheWholeSet"
```

Expect a passing run. The number of cases can change as exercises are added;
read the actual test output instead of memorizing a fixed count.

## Prove you understand the assertion

Temporarily change only your new expected array from `[2]` to `[0, 2]`. Run the
same command. It should fail because you now incorrectly expect an Open
conversation in a Closed-only result. Read the assertion failure, then restore
your line to `[2]` and rerun.

Restore only the line you edited. This checkout may contain other uncommitted
work; do not discard an entire file to undo your small experiment.

## Say what the test proves

Try: “In this fixture, a conversation must satisfy the state and tag conditions
together. Both API reads must agree with the expected set.”

Now say what it does not prove: it does not measure a million-row database,
freeze a changing queue, or validate every possible filter combination.

## Why the test does not use your demo database

[HarborApiFactory](../tests/Harbor.Tests/Integration/HarborApiFactory.cs) hosts the
application for integration tests with an in-memory SQLite connection. The
factory also replaces the real webhook sender with a fake. The test fixture
creates its own workspaces and IDs instead of relying on your local demo.

The test schema comes from `EnsureCreated`; Development startup replays
migrations. The existing migration-drift tests guard part of that difference.
Learn those details after you can explain your one-line case.

## Finish with a small note

Write the query, expected fixture index, exact command, observed result, and
one limitation. Then open your diff and identify your single addition.

Repeat: **arrange a world, perform an action, compare with an expectation**.
Next: [hear a pairing conversation](12-pairing-dialogues.md).
