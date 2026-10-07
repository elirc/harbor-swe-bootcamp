# Capstones: practice independent ownership

These were written as proposed exercises before the 75-story bootcamp.

> **Status note (checked 2026-10-06):** several capstones now have a reference
> implementation in this checkout. Treat them as "design it yourself first, then
> compare" exercises, and do the comparison on a practice branch.
>
> | Capstone | Reference implementation now in the code | Evidence |
> | --- | --- | --- |
> | 1. Waiting filter | `AwaitingFirstResponse` in `src/Harbor.Api/Contracts/Requests.cs` (story US17) | `QueueLearningTests.cs` |
> | 2. Message paging | `ConversationsController.ListMessages` — `GET /api/conversations/{id}/messages` (US22/US23) | `QueueLearningTests.cs` |
> | 3. Wake due snoozes | `TriageController.WakeDue` (admin-only, manual, `limit` 1–200) calling `SnoozeWake` (M08) | `TriageLearningTests.cs` |
> | 4. Stale browser edit | `ConversationEditingController` — `PUT .../subject` requires `ExpectedVersion`, returns 409 when stale (M04) | `SubjectEditingLearningTests.cs` |
> | 5. Email retries | `InboundEmailReceipts` lookup in `EmailController` (S02) | `S02InboundReceiptTests.cs` |
> | 6. Outbox worker | Partly: `WebhookDispatcher.DispatchAsync` now claims at most 50 leased deliveries one at a time (S03), but there is still **no hosted loop** — dispatch is a manual admin POST | `WebhookLeasePersistenceTests.cs` |
>
> Capstones 7 and 8 remain open. Choose one at a time.
Write a [feature brief](templates/feature-brief.md) before coding. Every capstone
needs acceptance criteria, tests, docs, a failure case, and a review of scope.
Time estimates are practice-session budgets, not delivery promises.

## 1. Add a precise waiting filter

Level: early mid. Budget: two 60-minute sessions. Start in `ConversationFilterRequest`
and `ConversationQueries`. Proposed behavior: `awaitingFirstResponse=true` selects
conversations without a recorded first teammate reply.

Decide whether closed conversations count and what `false` means. Preserve
omission behavior. Acceptance tests must cover open unanswered, closed unanswered,
answered, note-only, other tenant, and combination with a creation window.
List and overview must agree with independent expected sets.

Hint: add membership once. Deliver a small API change, a test table, and a
paragraph explaining the chosen semantics. Review question: could a user confuse
this filter with “currently overdue,” and how does the documentation prevent it?

## 2. Paginate conversation messages

Level: mid. Budget: three sessions. Start in `GetById`, `Message`, and `Paging`.
Current conversation detail includes the full thread; large threads can grow
without a response-size bound.

Propose `GET /api/conversations/{id}/messages` with bounded paging and a unique
chronological order. Preserve the current detail contract initially; removing its
thread is a separate compatibility decision. Define whether internal notes appear
for authenticated agents and keep the endpoint out of the public help center.

Acceptance tests: own conversation, foreign and unknown IDs returning the same
404, tied timestamps, empty/last/huge pages, correct total, and note visibility.
Inspect SQL to ensure you do not load the entire thread before slicing it.

Deliver the endpoint, tests, docs, and an adoption plan for clients of the old
detail response. Review question: how would a client append new messages while
someone else writes into the thread?

## 3. Wake due snoozed conversations

Level: mid. Budget: three or four sessions. Start in `Conversation.Snooze` and
`Conversation.Open`. Current snooze timestamps do not trigger an automatic sweep.

First implement a manually triggered, tenant-scoped, bounded sweep. Define whether
admins only or agents may trigger it. Use a controllable clock. Reopen only rows
still snoozed whose due time has arrived. Preserve the original SLA clock and
define which audit/event behavior is required.

Acceptance tests: just before, exactly at, and just after due; already open;
closed; foreign tenant; repeated sweep; and a conflicting state change between
load and save. Decide whether a conflict aborts the batch or produces per-item
outcomes. Do not hide a partial result behind a successful count.

Deliver the feature and a separate proposal for scheduling. Review question:
if the process crashes halfway through the batch, what safely happens next?

## 4. Protect a stale browser edit

Level: mid to senior. Budget: four sessions. Start in `IHasVersion`, `HarborDbContext`,
and `ConcurrencyTests`. Current database tokens do not travel with client edits.

Design a conditional-write contract. Decide the exact response representation
covered by its version, how clients obtain a token, whether supplying it is
optional during adoption, and which missing/malformed/stale conditions map to
which HTTP status. Study the HTTP standard before naming headers or status codes.

Acceptance tests: client A reads, B updates, A submits the old token; a fresh
token succeeds; foreign resources remain hidden; malformed tokens fail; and
message/tag changes either invalidate the chosen representation or are explicitly
outside its scope. Retain database concurrency protection for the race between
checking a token and saving.

Deliver an ADR, implementation, tests, and a client recovery example. Review
question: does the token identify the entity row or every byte of a response
containing related tags and messages?

## 5. Make inbound email retries safe

Level: mid to senior. Budget: four sessions. Start in `EmailController`, `Message`,
and `ChannelParityTests`. Inbound threading and request deduplication are distinct.

Define the identity of a duplicate: workspace, provider, and message identifier
may matter. Decide behavior for missing IDs and for reusing an ID with a different
payload. Use a database uniqueness constraint where appropriate; a pre-insert
lookup alone can race. Include a migration and an upgrade-data strategy.

Acceptance tests: repeated identical request, same ID in another tenant, changed
payload, absent ID, two racing requests, and exactly the intended message/outbox
count after retry. Use deterministic coordination, not random sleeps.

Deliver a contract, migration, tests, and a rollback/adoption plan. Review
question: what will the provider do after a timeout when it does not know whether
the first request committed?

## 6. Build a bounded outbox worker

Level: senior practice. Budget: five sessions, beginning with design. Start in
`WebhookDispatcher` and the outbox tests. First separate one bounded batch from
the scheduling loop. Define ownership, cancellation, retries, leases or claims,
and recovery after a crash. Use separate database scopes for independent work.

Acceptance criteria: bounded memory, no unbounded parallel requests, expired work
can be recovered, paused subscriptions do not consume attempts, one bad receiver
does not block every tenant, and shutdown has a documented deadline. Acknowledge
that receiver deduplication is still needed for send-before-save failures.

Deliver a failure timeline, implementation, integration tests, and a runbook with
oldest pending age and exhausted-attempt signals. Review question: how do you
distinguish no traffic from a stopped worker?

## 7. Measure and improve a large-tenant query

Level: senior practice. Budget: four sessions. Start in `ConversationQueries` and
`ReportsController`. Choose one workload, dataset size, and user-visible budget.
Measure before selecting an index, projection, aggregate, or cursor strategy.

Acceptance criteria: representative synthetic data, correctness parity, query
plan evidence, repeated timings, memory/result-size observations, and a migration
if indexing changes. State what happens under concurrent writes and what the
local machine cannot tell you about deployment performance.

Deliver a short experiment report containing a rejected approach. Review
question: what write cost or maintenance burden did your read optimization add?

## 8. Design an authorization and audit improvement

Level: senior practice. Budget: three design sessions, implementation afterward.
Choose one administrative operation such as key rotation or bulk reassignment.
Define who may act, what must be recorded, retention needs, and how errors avoid
revealing another tenant's data. Audit records should retain useful actor identity
without storing credentials.

Acceptance criteria: role/workspace matrix, successful and rejected actions,
atomic state/audit persistence where required, and a readable operator workflow.
Deliver a threat-boundary sketch, ADR, tests, and support instructions.
Review question: what can an operator establish from the audit trail, and what
would still require other evidence?
