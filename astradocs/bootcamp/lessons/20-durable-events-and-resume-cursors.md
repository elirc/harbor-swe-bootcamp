# Lesson 20: Durable events and resume cursors

S14 adds a resumable workspace event stream. Read [event staging](../../../src/Harbor.Infrastructure/WorkspaceEvents.cs), [cursor protection](../../../src/Harbor.Api/Infrastructure/EventCursors.cs), [the stream controller](../../../src/Harbor.Api/Controllers/WorkspaceEventsController.cs), and [stream tests](../../../tests/Harbor.Tests/Integration/S14WorkspaceEventStreamTests.cs). See [progress](../progress.md) for verification evidence.

## Separate the fact from its delivery

A conversation was created. That is a committed fact. A client received a notification about it. That is a delivery observation. They are not the same operation.

The API stages a WorkspaceEvent alongside the conversation or message mutation. SQL commits them together. Later, a stream connection polls committed event rows and writes server-sent-event frames. If the server stops after SQL commit but before writing a frame, the event remains available for replay.

Draw four arrows: application write → database commit → stream poll → client remembers cursor. Put a possible crash between every pair. The database log handles the gap between commit and streaming. Client cursor persistence handles reconnect progress. Neither produces a promise that a client sees an event exactly once.

The supported event kinds are ConversationCreated, MessageCreated, StateChanged, and AssignmentChanged. Metadata contains IDs and allowlisted state/assignment information. It does not copy message bodies. Priority and tag changes are outside this event contract.

## Why sequence is different from time

Two events can have the same RecordedAt. Clocks can also move backward. Sorting solely by timestamp cannot reliably answer “what came after the last event I consumed?”

The database assigns an increasing integer Sequence. It is global across workspace rows. Suppose workspace A owns sequences 10 and 14, while B owns 11–13. A's reader asks for A's rows after 10 and receives 14. The gap is normal. Never assume workspace sequence numbers are consecutive.

Each row also has a stable EventId. Clients use that event identity to deduplicate repeated facts. The SSE `id` field is a protected resume cursor, which may be refreshed when emitted; the JSON `eventId` remains stable across replay. Keep these roles distinct when writing a client: one identifies a fact, the other authorizes a position from which to resume.

## Read a cursor as a small contract

The protected payload includes format version, workspace ID, sequence, and expiry. Protection uses ASP.NET Core Data Protection with persisted keys. Base64 alone would only change the text representation; it would not prevent a caller from editing another workspace or a larger sequence into the payload.

`EventCursors.Read` checks both cryptographic protection and semantic fields. A validly protected token for workspace B is still invalid for workspace A. An expired token gives 410. Malformed, tampered, or wrongly scoped tokens give 400.

The key ring must survive restarts and be shared across instances issuing and consuming cursors. The cursor test creates a second provider against the same directory and reads the first provider's token. That is stronger evidence than issuing and reading it through the same object in memory.

## First connection and reconnect

Without Last-Event-ID, the stream starts at the workspace's current high-water mark and sends a ready frame with a cursor. It does not replay the entire historical log. A client can first load ordinary API state, then use the stream as an update feed according to its own synchronization design.

On reconnect, the client sends its saved cursor. The server reads later workspace events in sequence order, at most 100 per database poll. Each poll uses a short-lived context; it does not leave a database transaction open while waiting for the next event.

Heartbeat comments keep the connection active but carry no event ID. Saving a heartbeat as progress would be meaningless because no new committed fact was delivered. The connection ends after at most five minutes so a reconnect passes through authentication again. Revoking a key does not retroactively reauthenticate a request already streaming.

## History has a boundary

Events are eligible for pruning after seven days. The Admin prune command removes at most 1,000 events from an expired sequence prefix and advances a per-workspace purged-through watermark in the same transaction.

Why a prefix? Consider four events in sequence order: old, recent, old, old. A query that selects every old row could remove the third and fourth while retaining the second. A single watermark would then pretend a continuous prefix had been removed, hiding retained history and making resume reasoning wrong. The implementation stops when it reaches the first retained row.

If a cursor points before the watermark, the API returns 410 with a refresh instruction. It does not silently send whatever rows remain and pretend the client has a complete history. The client refreshes ordinary APIs and reconnects without the obsolete cursor.

## Trace the writers

ConversationStarter stages creation plus opening-message events. MessageAppending stages MessageCreated and an actual resulting state transition, such as reopening a closed conversation on contact reply. Manual assignment records AssignmentChanged only when assignment IDs change. State commands and snooze waking record actual state changes.

These calls are independent of webhook subscriptions. A workspace with no webhook URL still needs its durable event log. Conversely, adding this feed does not make webhook transport exactly once. They serve different delivery contracts.

Find an intentional no-op in the tests or code. Predict whether it should create a stream event. Compare the old and new state values rather than merely checking that an HTTP endpoint was called.

## Practice in three passes

Pass one: read the streaming test's frames. Identify ready, conversation creation, and opening message. Decode only the JSON data; treat the protected cursor as opaque client state. Verify that foreign interleaved rows and message bodies are absent.

Pass two: inject a failure on an event INSERT while appending a message. Inspect both Messages and WorkspaceEvents. The failed command must leave neither the new message nor its event. A separate post-commit event save would fail this exercise.

Pass three: arrange equal timestamps and a mixed-age sequence prefix. Prune once and predict the watermark. Reconnect from a cursor before it and observe 410. Then explain the recovery path to someone who has never used SSE.

Write this teach-back in your journal: “The event row is the durable fact. Sequence orders facts. EventId deduplicates them. The protected cursor resumes a scoped position. Pruning makes missing history explicit.” Repeat tomorrow using the actual classes and one concrete sequence diagram.
