# Lesson 07: SLA decisions and time boundaries

Stories: M17-M18. These features expose decisions already made by the SLA engine
and identify obligations that are about to become due. Their [verification status](../progress.md)
is tracked separately from this explanation.

## Begin with two different questions

An administrator asks, "If a High-priority conversation started in this inbox at
10:00, which targets would apply?" That is simulation.

An agent asks, "Which unresolved targets on existing conversations are due in the
next hour?" That is the upcoming queue.

Simulation reasons about supplied inputs without creating a conversation. The
upcoming queue reads actual persisted deadlines without recalculating them.
Changing configuration and observing obligations are related but distinct tasks.

## Select one policy, then describe its targets

Read [SlaPolicies.cs](../../../src/Harbor.Infrastructure/SlaPolicies.cs).
`Resolve` chooses the most specific matching policy, then breaks ties by creation
time and ID. `Describe` reports the selected target values or the inbox fallback.
Both real target application and simulation use that description.

| Available configuration | Result |
| --- | --- |
| Inbox+priority policy matches | Use that policy's targets |
| Inbox-only and priority-only policies match | Inbox-only wins on specificity |
| Matching policy has resolution only | First-response target remains absent |
| No policy matches, inbox has first-response minutes | Use InboxFallback |
| No policy matches, inbox has no target | Use None |

A null first-response target on a selected policy is not a request to borrow a
value from a less specific policy or the inbox. Current Harbor selects a policy;
it does not merge several configurations. That distinction is a useful code-review
question whenever "fallback" appears in a requirement.

## Reuse arithmetic without persisting the example

The simulation action creates an untracked temporary `Conversation` with the
supplied creation time and calls the domain target calculation. It never adds
that object to the context or saves it. Reusing a domain method does not require
creating a database row.

For a creation instant of 10:00 and target 30 minutes, the due instant is 10:30.
The time when the simulation request arrives is irrelevant. `05:00-05:00` and
`10:00Z` describe the same creation instant and therefore the same deadline.

Required nullable request fields distinguish an omitted priority or creation time
from a default value. Bad shape returns 400. If adding a target would exceed the
supported timestamp range, the action catches that arithmetic failure and returns
422. It does not return a fabricated deadline or persist a partly calculated row.

## A conversation can owe two things

An unanswered, unresolved conversation may owe a first response and a resolution.
The upcoming queue returns one row per target so neither obligation disappears.

At 10:00, a thread with first response due at 10:15 and resolution due at 10:45
contributes two rows to a one-hour upcoming queue. Its target count is two while
its conversation count is one. This is intentional multiplicity, unlike the
accidental parent duplication discussed in earlier aggregation lessons.

Read [SlaDiagnosticsController.cs](../../../src/Harbor.Api/Controllers/SlaDiagnosticsController.cs).
The endpoint first applies the existing conversation filters. It then excludes
Closed conversations, projects unresolved first-response targets and unresolved
resolution targets, combines the two sequences, and pages the complete target
sequence.

The pattern should now look familiar from the timeline: combine comparable keys
before ordering and paging. The source rows represent obligations instead of
historical events.

## Name the interval before writing comparisons

For a captured `now` of 10:00 and interval 60 minutes, the window is
`[10:00, 11:00)`: include the start, exclude the end.

| Target due time | Included? | Reason |
| --- | --- | --- |
| 09:59:59 | No | Already overdue |
| 10:00:00 | Yes | Lower boundary is inclusive |
| 10:30:00 | Yes | Inside the window |
| 11:00:00 | No | Upper boundary is exclusive |

The comparison must also require that the corresponding milestone is unresolved.
An old `FirstResolvedAt` remains completed after reopening, so its resolution
target does not become pending again. A note does not set `FirstRespondedAt`, so
it does not satisfy the first-response obligation.

Each request captures time once and returns it in `X-Evaluated-At`. Calling the
clock repeatedly while building predicates could give slightly different windows
for the two target kinds. Capturing once makes the question precise.

## Why the tests freeze only the clock they need

The new paths receive a `TimeProvider`. The test host can supply a fixed UTC
instant for these paths while existing production code continues using its
established clocks. This is a focused introduction of controllable time, not a
claim that every timestamp in Harbor now comes from the provider.

Read [SlaLearningTests.cs](../../../tests/Harbor.Tests/Integration/SlaLearningTests.cs)
and the clock hook in [HarborApiFactory.cs](../../../tests/Harbor.Tests/Integration/HarborApiFactory.cs).
The upcoming test sets a fixed instant, arranges exact target times, checks both
pages, and resets the clock in `finally`. Resetting shared test state prevents one
test's setup from silently changing later tests.

## Work the examples again without the source

1. An inbox has 60-minute fallback; its matching policy has resolution=30 and
   first-response=null. Predict both simulated due times for creation at 10:00.
2. A conversation has two future targets but already has `FirstRespondedAt`.
   Predict how many upcoming rows remain.
3. A reopened conversation has `FirstResolvedAt`. Explain why that milestone is
   still completed.
4. The caller requests `state=Closed`. Explain why the upcoming queue can correctly
   return an empty set after applying its additional non-Closed restriction.
5. The first page contains one target and total count two. Explain why the second
   page can legitimately point to the same conversation.

Answers: no first-response due time and resolution due at 10:30; only the
unresolved resolution target can remain; reopening preserves the first milestone;
the combined state conditions contradict each other; each row is an obligation,
so one conversation may occupy two positions.

For your journal, write one sentence distinguishing **policy selection**,
**deadline calculation**, **milestone completion**, and **current state**. Use the
same conversation in all four sentences. That separates concepts that often get
accidentally combined in SLA features.
