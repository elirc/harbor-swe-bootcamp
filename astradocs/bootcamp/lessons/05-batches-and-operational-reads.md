# Lesson 05: batches and operational reads

Stories: M05, M06, M08, M09, M10. This lesson follows the staged implementation;
check the [tracker](../progress.md) for test results. M07 inbox transfer is a
separate schema-bearing change and is not implicitly included in this lesson.

## One request can have many consequences

Changing priority also applies an SLA policy and detects breaches. Replacing tags
updates relationships. Waking snoozes changes state. Reading a timeline combines
several sources, while eligibility diagnostics must explain assignment without
performing one.

The common review question is: **what is the complete effect of this operation?**
Name every row it may change, every rule it must reuse, and the condition under
which it should change nothing.

## Atomic batches, explained with two conversations

You select C1 and C2 and ask for Urgent priority. A loop of independent HTTP calls
could update C1 and then fail on C2. The user would have to discover the partial
result. The bulk contract instead requires both changes to commit together.

The endpoint validates the request shape, loads all selected rows within the
workspace, checks that every ID was found, stages the shared priority workflow,
and saves once. Any unavailable ID produces 422 before the first mutation.

The database transaction inside `SaveChangesAsync` provides a second protection:
if an optimistic concurrency check fails during the save, earlier statements in
that same save roll back too. Checking IDs before mutation and committing
atomically solve different failure points; neither replaces the other.

Read [PriorityChanges.cs](../../../src/Harbor.Infrastructure/PriorityChanges.cs),
then the single-item action in [ConversationsController.cs](../../../src/Harbor.Api/Controllers/ConversationsController.cs)
and `BulkPriority` in [TriageController.cs](../../../src/Harbor.Api/Controllers/TriageController.cs).
Both call the same staging helper. The helper does not save; its caller chooses
the commit boundary.

Repeat the idea using a bank's internal ledger as an analogy: two related row
changes belong to one operation. Then return to Harbor and state the actual rows:
conversation priority, SLA deadlines, and newly detected breach records. Do not
extend the analogy into claims about this application handling financial transfers.

## The SLA clock still starts at creation

A conversation created at 10:00 with a new 15-minute target becomes due at 10:15,
even if the bulk command arrives at 11:00. Moving the deadline to 11:15 would
restart the customer's waiting clock. The shared resolver prevents the bulk
feature from inventing a different interpretation.

Two conversations in different inboxes can receive different target durations
even when their new priority is identical. The test arranges inbox-specific
policies and checks deadlines against each conversation's creation timestamp.

The separate-connection rollback test in
[PersonalWorkPersistenceTests.cs](../../../tests/Harbor.Tests/Unit/PersonalWorkPersistenceTests.cs)
loads two rows, lets another context change one, then attempts the stale batch.
Read the final assertions through a fresh context: tracked in-memory values after
a failed save are not proof of database contents.

## Replacing a set means retaining its intersection

Current tags are `{A, B}`. Requested tags are `{B, C}`.

```text
remove = current - requested = {A}
add    = requested - current = {C}
keep   = current intersect requested = {B}
```

Only conversation-to-tag links change. Tag A still exists for other
conversations. An empty requested set removes all links, while a null request is
invalid. Duplicate IDs are rejected before converting to a set, because a set
would otherwise hide the malformed input.

The replacement action opens a transaction before reading the parent and tags.
It checks every requested tag before removing anything, changes the relationship
difference, saves, then commits. The response must reflect the resulting
collection; a stale tracked navigation could otherwise return removed tag names.

This command has no client version precondition. A later successful replacement
may intentionally replace an earlier selection. Atomicity means each replacement
is complete; it does not mean the server can infer how two people wanted their
selections merged.

## A timestamp does not execute a transition

A Snoozed row can store `SnoozedUntil=12:00` indefinitely. A query can find it at
12:01, but finding it does not reopen it. `SnoozeWake` selects due work and calls
the existing `Open(now)` transition, then saves one bounded batch.

Read [SnoozeWake.cs](../../../src/Harbor.Infrastructure/SnoozeWake.cs). Time is an
explicit method argument, which makes an exact boundary test possible without
sleeping. The HTTP action obtains time from `TimeProvider`. Only this new path is
changed; the codebase's older clock usages remain visible for later discussion.

With rows due at `now-1 tick`, `now`, and `now+1 tick`, the first two are eligible.
The comparison is inclusive. A limit bounds the amount of work performed; another
call can drain the next batch. This feature is an explicit Admin command and does
not install a hidden scheduler.

Opening a snoozed conversation changes state and metadata time, not message
activity. Tests compare `LastMessageAt` before and after waking.

## Build the page of the union, not the union of pages

The timeline includes messages, assignment history, and SLA breaches. Imagine
each source sorted independently. Taking page two from each source and merging
them is not page two of the whole timeline: the relative positions depend on all
three sources together.

The implementation projects each source to the same small key shape:
`(ID, source kind, occurred time)`. SQL `Concat` combines all keys, the shared
paging helper counts and selects the global page, then at most three bounded
queries load payloads for those selected keys. The response follows the key order,
not the order in which each payload query happened to return rows.

Read [TimelineController.cs](../../../src/Harbor.Api/Controllers/TimelineController.cs).
Explain why identity includes source kind: a message and assignment can share the
same GUID and still be two different events. The test deliberately creates that
case. Tied times order Message before Assignment before SlaBreach, then by ID.

Breach occurrence uses `BreachedAt`, not when the breach row was inserted. The
timeline cannot reconstruct unrecorded subject edits or state changes. A combined
read is not automatically a complete audit history.

## Explain eligibility without consuming a turn

`AutoAssigner.PickNextAsync` updates the inbox rotation pointer. Calling it from a
diagnostic GET would make observation change assignment behavior.

Instead, [AssignmentEligibility.cs](../../../src/Harbor.Infrastructure/AssignmentEligibility.cs)
contains a pure calculation and a reusable grouped count query. The picker and
diagnostic path use the same eligibility rule. The diagnostic path never invokes
the picker.

| Availability | Assigned non-Closed count | Limit | Reasons | Eligible |
| --- | --- | --- | --- | --- |
| Available | 1 | 2 | None | Yes |
| Available | 2 | 2 | AtCapacity | No |
| Away | 1 | 2 | Away | No |
| Away | 2 | 2 | Away and AtCapacity | No |
| Available | 200 | null | None | Yes |

Open and Snoozed assignments count; Closed and team-only assignments do not count
toward an individual. Null means unlimited. Eligibility still does not predict
the next rotation winner.

## Practice and review

Read [TriageLearningTests.cs](../../../tests/Harbor.Tests/Integration/TriageLearningTests.cs).
For each test, write one plausible defect it catches and one claim it cannot prove.

1. An unavailable second bulk ID catches partial mutation before validation.
2. A `{B,C}` replacement catches accidentally deleting the Tag records.
3. An exact-time snooze catches a mistaken strict comparison.
4. Same GUID in two timeline sources catches using only ID as dictionary identity.
5. Repeated eligibility GETs catch accidental rotation-pointer updates.

Then explain each feature with only two or three rows on paper. Keep repeating
the smallest example until you can predict the failure, then return to the code.
