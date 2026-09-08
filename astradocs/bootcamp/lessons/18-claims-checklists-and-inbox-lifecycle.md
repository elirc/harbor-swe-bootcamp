# Lesson 18: Claims, checklists, and inbox lifecycle

S18–S20 share a practical idea: **a command must check the state that its own commit depends on**. Read [ClaimNextController](../../../src/Harbor.Api/Controllers/ClaimNextController.cs), [ChecklistsController](../../../src/Harbor.Api/Controllers/ChecklistsController.cs), and [InboxArchiveController](../../../src/Harbor.Api/Controllers/InboxArchiveController.cs). The [tracker](../progress.md) separates staged implementation from verified behavior.

## Explain it first as support work

An available teammate clicks “claim next.” They should receive one suitable unassigned conversation. Two teammates clicking together should not both receive the same conversation. A teammate with one free capacity slot should not acquire two threads through competing clicks.

A teammate adds “verify refund” as a required checklist item. Closing the conversation should name that incomplete item and reject the close. An optional survey does not block closure. Completing a checklist item does not itself close the thread.

An Admin archives an old inbox. New conversations should stop arriving, but existing customers still need replies. The inbox, its address, and its history remain. Archival is a lifecycle state, not deletion.

These sound like separate features. Their correctness depends on the same pattern: read current state, decide, stage all related changes, and commit together.

## Why two ordinary queries can still be wrong

Imagine A and B each read conversation C as unassigned. Both decide to assign it to themselves. If those reads happen outside their assignment transactions, both decisions were based on the same old state. A final UPDATE alone does not explain which decision remains valid.

Claim-next obtains the SQLite writer reservation before reading availability, workload, or candidates. It counts the caller's non-Closed assignments, including Snoozed work. It then selects one Open conversation with neither a team nor teammate assignment, ordered by priority descending, creation time ascending, and ID.

The caller comes from authentication claims. There is no target-teammate request field. This command means “claim for me,” not “assign to any person.” It also does not call the round-robin picker, which would choose someone else and move the inbox pointer.

`ManualAssignments.StageAsync` applies the assignment and stages its normal history and webhook effects. The caller saves before returning the summary. A rollback leaves no claimed owner or orphan assignment history.

The command does not create a universal capacity rule. Existing manual assignment can retain its own product semantics. Say precisely what the new guarantee covers: competing claim-next commands use a current capacity decision inside their transaction.

## A checklist invariant crosses two tables

Write this invariant in your own words: “A supported command cannot close a conversation while also allowing a newly committed incomplete required item against that same closed state.”

The checklist item has its own version for stale item edits. But closure changes the parent conversation. If an item edit never changed the parent version, a close writer could miss the relationship between the two operations.

Every actual item mutation therefore touches the parent UpdatedAt and forces a parent update. That force matters under a fixed test clock: assigning the same timestamp value might otherwise look unchanged to EF. The parent's Version must still change. LastMessageAt remains untouched because checking a task is not sending a message.

Close and item mutations use short writer transactions. Item mutations check that the parent is not Closed. Close loads incomplete required IDs before calling the existing lifecycle method. A blocked close stages no closed webhook or breach effects. The item limit of 50 is checked in the same transaction as insertion.

Make this table from memory before reading tests:

| Items | Close result |
| --- | --- |
| None | Allowed |
| Incomplete optional item | Allowed |
| Incomplete required item | 422 with blocker ID |
| All required items complete | Allowed |

After closing, even optional items cannot be edited until reopening. Reopening retains checklist completion and the conversation's original first-resolution milestone.

## Creation and continuation are different decisions

Draw an inbox containing one existing email thread. Mark the inbox archived. Now draw two incoming emails: a valid reply referencing that thread, and a new question with no matching thread.

The valid reply may continue the existing conversation. It still must match both the selected inbox and the conversation contact's normalized email. A reference to somebody else's thread is not permission to append to it.

The new question is rejected before creating a sender contact. Otherwise a denied conversation could leave an orphan customer row. A previously committed inbound receipt replays before the archive guard; retrying an old successful request is not starting fresh work.

`InboxLifecycle.RequireActiveForNewWork` is shared by conversation creation and transfers into a destination inbox. The active check sits inside the transaction that commits new work. Archive uses the same writer boundary. Therefore the two commands have a concrete order: either creation commits first, or archive commits first and creation sees it.

Default inbox listing hides archived rows. That is only a presentation choice. The actual lifecycle guarantee comes from guards on every creation entry point. GET-by-ID and historical conversation/report queries retain access to existing work.

## Rehearse the races as timelines

For claim-next, use two HTTP clients and a disposable file database with separate connections. Release both calls from a gate. With two candidates and one remaining capacity slot, inspect the database and assert at most one new assignment to that teammate. A shared in-memory connection is not evidence of independent-connection contention.

For checklist closure, race close against adding a required item. The acceptable outcomes differ in which command wins. The forbidden outcome is Closed plus a newly accepted incomplete required item. Assert the invariant, not a scheduler-dependent winner.

For archival, race archive against creation. Both can succeed if creation committed first. A successful creation after archive's state was already observed must be rejected. Again, test the durable order and final rows instead of assuming which task ran first.

The shared transaction helper has a bounded busy retry policy. A 503 with Retry-After is a transient outcome, not a successful command. Do not blindly retry a claim after losing a successful HTTP response: claim-next has no idempotency receipt and a retry might claim another conversation.

## Repeat the learning in three forms

Product form: “Can this teammate take more work? Can this issue close? Can this inbox accept a new thread?”

Data form: “Read availability and non-Closed workload; read required child items and parent state; read ArchivedAt before creating rows.”

Failure form: “If another writer changes the relevant state, my command must recheck under its transaction or fail its version check. Related history and outbox effects must roll back with it.”

Write all three forms for one feature without looking. Then open the tests and identify which assertion proves each sentence. Record missing evidence honestly in the [journal](../journal.md). That exercise develops the habit of linking product promises to executable evidence.
