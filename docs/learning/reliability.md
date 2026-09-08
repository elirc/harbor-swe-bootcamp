# Reliability: reason about time, transactions, and retries

Goal: identify a system's actual guarantee and a failure it cannot prevent.
Prerequisite: trace `AddMessage` from HTTP input through `SaveChangesAsync`.

## A transaction joins database changes

Harbor queues webhook deliveries in the same database save as the conversation
or message change. [Webhooks.cs](../../src/Harbor.Infrastructure/Webhooks.cs)
adds pending delivery entities; it does not send HTTP inline. The
[transactional outbox ADR](../adr/0003-transactional-outbox.md) explains the choice.

The useful invariant is that the database change and its queued delivery commit
together. It avoids “saved the conversation but crashed before queuing the event.”
It does not make the subscriber's HTTP server part of the SQLite transaction.

Exercise: draw a table with crash points before save, during a rolled-back save,
after commit, after HTTP success, and after delivery-status save. For each point,
state whether the conversation, outbox row, and external effect exist.

## Optimistic concurrency compares versions

`Conversation` and `WebhookDelivery` implement `IHasVersion`.
[HarborDbContext](../../src/Harbor.Infrastructure/HarborDbContext.cs) changes the
version when a tracked entity is modified. EF compares the original token during
the update; another writer changing it can cause `DbUpdateConcurrencyException`,
which the API maps to 409. See Microsoft's [concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
for the underlying EF behavior.

Work through two writers:

| Step | Writer A | Writer B |
| --- | --- | --- |
| 1 | Loads version V1 | Loads version V1 |
| 2 | Saves a change with a new version | Still holds V1 |
| 3 | Save succeeds | Update using V1 matches no row; conflict |

The current API protects overlapping tracked database writes. It does **not**
send an edit token to a browser and require it on a later request. If a human
reads a page, another human edits it, and the first later sends an update, that
new request normally loads the latest version. Client-visible stale-edit
protection needs a separate contract, such as conditional writes.

Exercise: explain why an automated blind retry can overwrite a user's intent.
What would the client need to refresh or reconcile after a 409?

## A retry can repeat a completed external effect

`WebhookDispatcher` loads due deliveries, sends them, and saves their outcomes.
The receiver may accept a request even if Harbor never observes the response.
A crash after sending and before saving can cause another attempt. Two dispatchers
can also send the same row before a token conflict is detected during save.

Receiver deduplication should use a stable delivery identifier, not the signing
timestamp, which can change per attempt. HMAC verification establishes integrity
and a timestamp tolerance limits old replays; neither alone establishes that a
delivery has never been processed before.

Exercise: design a receiver transaction that records the delivery ID and applies
the intended effect together. Explain why “check if processed, then act, then
record” can race when those steps are not atomic.

## Scheduling and bounded work are separate concerns

The repository exposes manual endpoints to evaluate SLAs and dispatch webhooks.
There is no hosted scheduler that automatically drains the outbox. A snoozed
conversation also has no periodic wake-up sweep yet. Storing a due time does not
make an operation run when that time arrives.

The existing dispatcher materializes all due deliveries for a workspace and
processes them serially. Retry attempts are limited, so do not promise eventual
delivery in all cases. A future worker needs bounded batches, cancellation,
recovery, observability, and an explicit policy for exhausted deliveries.

Exercise: estimate the time for 500 serial deliveries if every attempt hits a
10-second timeout. Propose a batch limit and a recovery signal before proposing
parallelism. A single EF `DbContext` should not be used for concurrent operations.

## The clock is an input

Domain methods already accept `now`; the shared conversation query does too.
Many controllers still read `DateTimeOffset.UtcNow` directly. A `TimeProvider`
refactor is an opportunity to make application timing tests deterministic, but
it should preserve deadline semantics and not change all behavior at once.

Exit criteria: explain the current guarantees in plain language and draw one
failure that remains possible. Choose a related [capstone](capstones.md).
