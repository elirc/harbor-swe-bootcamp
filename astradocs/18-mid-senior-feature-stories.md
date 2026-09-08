# 20 mid/senior feature stories: own the behavior across boundaries

**Original planning briefs, now accompanied by implementation.** The source contains these workflows, with worked explanations in the [implementation bootcamp](bootcamp/README.md). Consult its [verification tracker](bootcamp/progress.md) for actual test checkpoints and the [API reference](../docs/api-reference.md) for integrated contracts. The plans retain their original starting-point descriptions and suggested names so you can compare design intent with implementation.

This backlog follows the [junior stories](16-junior-user-stories.md) and [midlevel stories](17-midlevel-feature-stories.md), but does not assume any of them have been built. These are new capabilities, not renamed versions of those exercises. The emphasis is on owning correctness across requests, transactions, workers, data upgrades, and user expectations.

The instructions are deliberately small and explicit. The engineering is still substantial. “Easy to follow” means you always know the next step and what evidence to collect; it does not mean distributed effects, migrations, or concurrency become trivial. Work on one story at a time and split its numbered steps into several small pull requests when appropriate.

## Pick a feature

| Story | Feature request | Main engineering lesson | Persistence change | Required dependency |
| --- | --- | --- | --- | --- |
| [S01](#s01) | Retry conversation creation without creating duplicates | Durable idempotency and response replay | Request receipts | None |
| [S02](#s02) | Replay inbound email safely and inspect its receipt | Message identity and transactional ingestion | Inbound receipts | None |
| [S03](#s03) | Claim webhook work with expiring leases | Ownership before external effects | Delivery lease fields | None |
| [S04](#s04) | Inspect every webhook attempt, including uncertain outcomes | Attempt history and crash semantics | Attempt ledger | S03 |
| [S05](#s05) | Issue expiring API keys and revoke individual credentials | Authentication migration and secret lifecycle | Credential table | None |
| [S06](#s06) | Preview and commit a bounded contact merge | Referential integrity and stale plans | Merge plans and audit | None |
| [S07](#s07) | Validate and commit a batch contact import | Staging, row errors, and atomic commit | Import batches and rows | None |
| [S08](#s08) | Route new conversations using ordered contact rules | Rule precedence and channel parity | Versioned routing definitions | None |
| [S09](#s09) | Watch a thread and receive private reply notifications | Transactional fan-out and personal ownership | Watchers and notifications | None |
| [S10](#s10) | Schedule an article for publication | Durable scheduling and stale-content checks | Publication schedules and token | None |
| [S11](#s11) | Measure SLA targets in configured UTC working hours | Calendar arithmetic and immutable configuration | Working calendars and references | None |
| [S12](#s12) | Preview and reapply current SLA policies to selected work | Configuration migration and optimistic plans | Recalculation plans | None |
| [S13](#s13) | Attach small files to chat messages | Filesystem/database consistency and access | Attachment metadata and files | None |
| [S14](#s14) | Resume a workspace's live event feed after disconnecting | Durable replay and retention boundaries | Event log and checkpoints | None |
| [S15](#s15) | Request a consistent conversation export in the background | Job recovery and snapshot semantics | Export jobs and files | None |
| [S16](#s16) | Audit changes to support configuration | Actor attribution and safe audit data | Configuration audit log | None |
| [S17](#s17) | Edit published articles without changing the live page | Draft/live separation and data migration | Publication snapshots | None |
| [S18](#s18) | Claim the next eligible unassigned conversation | Atomic selection and contested work | None required | None |
| [S19](#s19) | Require checklist completion before closing a thread | Cross-record invariants and concurrency | Checklist items | None |
| [S20](#s20) | Archive an inbox while keeping its existing history | Resource lifecycle and ingress coordination | Inbox archival field | None |

S04 is the only required dependency. S03 supplies durable attempt ownership before S04 adds its history. S10 and S17 can be built independently, but their final integration must publish through the same publication service if both exist. Likewise, S11 and S12 must share target calculation if both exist. Reuse helpers already introduced by another story rather than creating competing implementations.

Suggested progression: S16 or S19 first; S01 or S08 next; S03/S04, S13, S15, and S17 after you are comfortable tracing writes. S06 is deliberately bounded but still needs careful review because it changes references and removes a source contact.

## Read the architecture in three ways

**Follow a successful request.** A controller binds a contract, checks scope, loads data, invokes a rule, stages changes, saves, and maps a response. [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs) stages creation effects for both chat and email; callers commit them.

**Follow a failure.** Ask what has already happened when an exception occurs. A database rollback can remove a staged message and outbox row. It cannot unsend an HTTP webhook or undo bytes already written to a file. Draw the failure point beside each boundary.

**Follow an invariant.** Pick one sentence such as “one receipt identifies one committed creation” or “an unpublished draft cannot reach a public response.” Find every code path that could make the sentence false. Those paths define the required integration work and regression tests.

Use [follow a write](05-follow-a-write.md), [state and time](08-state-and-time.md), and [access and errors](09-access-and-errors.md) when those descriptions need another pass. Repetition is useful: explain the same story in user language, as a sequence diagram, and as a concrete database fixture.

## Shared implementation and verification routine

1. Read the linked source and tests before editing. Run `rg "MethodName" src tests` to find every caller of a rule you will change. New classes named in a story are suggestions, not existing APIs.
2. Write the story's invariant, input/output example, error cases, and bounds in a small feature brief. Copy the acceptance criteria into your issue. Identify exactly where the operation becomes committed.
3. Add the proposed `SXX...Tests` class under `tests/Harbor.Tests/Integration`. Run its filter and confirm at least one test was discovered and failed for the intended missing behavior. A zero-test result is not validation.
4. Build the smallest successful flow, then the specified retries/conflicts. Keep pure calculations in a testable helper; keep SQL filtering in the database; let the controller translate outcomes to HTTP.
5. For model changes, configure fields, keys, indexes, ownership, and deletion behavior in [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs). Add a migration with `dotnet ef migrations add SXXFeatureName --project src/Harbor.Infrastructure --startup-project src/Harbor.Api`. Inspect its generated operations and model snapshot.
6. Read [the migration/testing caveat](../docs/testing.md). Run `dotnet test --filter "FullyQualifiedName~MigrationDrift"` and upgrade a disposable database containing representative old rows from the preceding migration. `EnsureCreated` alone does not test an upgrade. Do not delete your working database to hide migration problems.
7. Run the story's focused tests, relevant existing regression classes, then `dotnet test --configuration Release`. After updating API/architecture docs during implementation, run `python scripts/check-doc-links.py`. These are future instructions, not claims that these features have passed tests.
8. Record observable outcomes, a rollout order, rollback limits, and an explanation of the failure cases. A test result on your laptop does not prove a hosted deployment or a multi-process guarantee.

### Repository details that matter for these plans

- [Requests.cs](../src/Harbor.Api/Contracts/Requests.cs) and [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs) are the initial homes for new API contracts. New domain entities belong under `src/Harbor.Domain/Entities`; new API orchestration can begin under `src/Harbor.Api/Infrastructure`.
- Get workspace/teammate identity from [claims](../src/Harbor.Api/Infrastructure/ClaimsPrincipalExtensions.cs), never from an owner supplied in the request. Normal foreign workspace routes give 403 through [WorkspaceScopeFilter](../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs); missing/foreign resource-by-ID lookups give 404. Missing credentials give 401; forbidden roles give 403. New routes must be included in authorization coverage.
- Use 400 for malformed shape, 422 for an unusable business request, and 409 for a stale or conflicting command, unless a story specifies otherwise. Use ProblemDetails. Validate undefined numeric enum values and distinguish omitted required booleans/value types from legitimate defaults.
- [ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs) creates a bootstrap Admin when it creates a workspace and switches the active client key. Create fresh workspaces per test, and switch back after arranging foreign fixtures. An opening conversation already has one contact reply.
- [HarborApiFactory](../tests/Harbor.Tests/Integration/HarborApiFactory.cs) shares one in-memory SQLite connection. Use it for ordinary HTTP behavior. For simultaneous connections, locks, and competing workers, add a **dedicated disposable SQLite file fixture with separate connections/contexts**, run migrations on it, and control interleavings with barriers. Sharing a DbContext or one connection across parallel operations is not a realistic concurrency test.
- [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs) deterministically tests stale tracked writes. It does not prove that two HTTP sends cannot happen: the current [dispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs) sends before saving outcomes. Preserve that distinction when evaluating S03/S04.
- SQLite has one writer. Where a story requires an immediate write transaction, acquire its write reservation **before the reads that determine the command**, enlist EF on that same connection/transaction, keep the transaction short, and perform no network/file streaming inside it. Retry known busy/lock conflicts at most three times with fresh transaction state, then return a documented 503 with Retry-After. Never retry an external side effect as though it were a rolled-back SQL statement.
- Use a clock passed into pure logic or an injectable `TimeProvider` for new orchestration. Tests advance the fake clock; they do not sleep. Existing date comparisons use the [UTC converter](../src/Harbor.Infrastructure/UtcDateTimeOffsetConverter.cs). SQL date ordering and in-memory GUID ordering must be tested against the actual provider.
- `Conversation.Version` protects overlapping entity writes. A stale-client guarantee also requires a client version or a stored plan fingerprint. EF bulk updates/raw SQL bypass the context's token bumping: explicitly include token conditions and update tokens when using those paths.
- [Paging](../src/Harbor.Api/Infrastructure/Paging.cs) returns arrays with `X-Total-Count`, `X-Page`, `X-PageSize`, and `X-TotalPages`; default size 50, maximum 200. Query membership, total, ordering, and page selection must agree. Job responses and event streams specify different contracts explicitly.
- [RequestLoggingMiddleware](../src/Harbor.Api/Infrastructure/RequestLoggingMiddleware.cs) already avoids request bodies and API keys. New logs should use workspace/entity/job/attempt IDs and outcomes. Do not add raw credentials, message bodies, attachments, or webhook secrets to diagnostic logs.

### How to break a large story into reviewable work

Use four checkpoints: **A, contract and failing examples; B, schema and pure rules; C, one integrated vertical slice; D, failure recovery and rollout evidence.** At each checkpoint explain what now works and what is still unimplemented. If a configuration switch is introduced, name it in the PR and keep it off until the migration and verification are complete; no such switches exist merely because this plan recommends them.

Every story below includes a concrete failure rehearsal. Write down the database rows, file state, and fake-sender calls expected before and after the interruption. Resume the operation and inspect them again. This is how to replace “it should be safe” with evidence.

<a id="s01"></a>
## S01: Make conversation creation safely retryable

**User story.** As an API client that sometimes loses a response, I want to retry starting a conversation with the same request key and receive the original result without opening another thread.

**Learn here.** Idempotency is a stored relationship between an operation identity, request meaning, and committed response. An in-memory dictionary or an existence check before insertion cannot provide the same guarantee across processes.

**Open first:** [ConversationsController.Start](../src/Harbor.Api/Controllers/ConversationsController.cs), [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs), [Webhooks](../src/Harbor.Infrastructure/Webhooks.cs), and [WebhookOutboxTests](../tests/Harbor.Tests/Integration/WebhookOutboxTests.cs). Create `S01CreationReplayTests.cs`.

**Contract and acceptance criteria.** The existing start endpoint optionally accepts `Idempotency-Key`, 1–128 ASCII letters/digits/`-`/`_`. Without it, existing behavior remains. Scope a key by workspace, authenticated teammate, and operation name. The first success returns 201; a matching retry returns the same 201 body and Location, plus `Idempotency-Replayed: true`. Reusing the key with different normalized input gives 409. Retain receipts for at least 24 hours, and keep replaying while a receipt remains; no cleanup is required in this slice. Authenticate every replay. Only successful creation responses are retained; shape/business failures do not consume the key.

**Implementation plan.**

1. Write a lost-response test: send once, discard the response in the test, send the same key again, then count conversations, opening messages, assignment events, and deliveries.
2. Define canonical request meaning from the validated start DTO: explicit property order, resolved default priority, exact body/subject semantics, and IDs. Hash that representation with a schema version; do not hash arbitrary raw JSON property order.
3. Add `CreationReceipt` with workspace, teammate, operation, key, request hash/version, response status/body/content type/Location, and timestamps. Add a unique index over the operation identity and owner scope; generate `S01CreationReceipts`.
4. For keyed calls, open a short immediate write transaction before looking up the receipt. A matching committed receipt can be replayed immediately. A different hash gives 409 before any domain work.
5. If absent, perform the ordinary scoped inbox/contact validation and invoke `ConversationStarter`. Stage the receipt and creation effects together. Serialize the same response contract that will be returned, with the API's configured JSON options.
6. Save and commit before writing the HTTP response. Never publish HTTP webhooks inside this transaction. A crash after commit but before response must leave enough receipt data for an exact replay.
7. Bound receipt response size to the known creation-response shape. Do not extend this mechanism to key-minting or arbitrary controller responses, which may contain one-time secrets.
8. Add same-key/different-input, reordered JSON, default priority equivalence, different owners, revoked/invalid authentication, foreign workspace, and no-key regression tests. If later authorization rules change, recheck current access before replaying protected content.
9. Use separate SQLite connections to race identical keys. Expect one committed creation and two equivalent successful outcomes, or a bounded transient 503 followed by a successful replay; never two creations.
10. Run focused/outbox/channel regressions, migration checks, and document the minimum retention window and behavior after an operator eventually removes an old receipt.

**Concrete fixture and failure rehearsal.** Key K creates C1/M1 and one ConversationCreated delivery. Simulate interruption after commit: retry returns C1/M1, with all counts unchanged. Fail before commit: no receipt or creation rows survive, and retry creates one fresh result. K with a changed body returns 409.

**Run:** `dotnet test --filter "FullyQualifiedName~S01|FullyQualifiedName~WebhookOutboxTests|FullyQualifiedName~ChannelParityTests"` plus migration checks.

**Rollout and limits.** Migrate first, enable keyed requests afterward, and retain receipt reads when rolling back new writes. Dropping the table breaks replay guarantees. Keys are scoped retry identities, not a global conversation deduplication algorithm.

**Teach it back / done when.** Explain why the receipt and conversation share one commit. The story is complete when response loss and concurrent retries cannot create duplicate committed conversation effects within the retained-key contract.

<a id="s02"></a>
## S02: Add durable inbound-email receipts and replay detection

**User story.** As a mail relay operator, I want duplicate deliveries to return the original ingestion result and a receipt I can inspect, so provider retries do not create duplicate customer messages.

**Learn here.** An inbound Message-ID identifies provider input; In-Reply-To identifies a possible ancestor. Those are different purposes. The current inbound controller threads by references but does not maintain a replay receipt.

**Open first:** [EmailController](../src/Harbor.Api/Controllers/EmailController.cs), [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs), [Message](../src/Harbor.Domain/Entities/Message.cs), and [EmailChannelTests](../tests/Harbor.Tests/Integration/EmailChannelTests.cs). Create `S02InboundReceiptTests.cs`.

**Contract and acceptance criteria.** When inbound `MessageId` is nonblank, normalize outer whitespace only and identify the ingestion by workspace, resolved inbox, and MessageId. Same identity plus same canonical input returns the original 200 `InboundEmailResponse`, without changing LastSeenAt or adding rows. Different input for that identity gives 409. Add receipt ID/replayed headers and scoped `GET api/inbound-email-receipts/{id}` returning IDs, ingest time, and outcome metadata, not the email body. Both roles retain existing ingestion permission. Omitted MessageId keeps legacy non-deduplicated behavior. A known referenced thread must belong to the selected inbox and its contact's normalized email must equal From, otherwise 422 before writes.

**Implementation plan.**

1. Arrange a routed inbox and send the same new-thread email twice. Assert one contact, one conversation, one message, and the original created flags on replay.
2. Define the canonical input: normalized From/To, trimmed MessageId/InReplyTo, ordered trimmed nonblank References, normalized FromName as used by ingestion, and exact subject/body. Preserve identifier case.
3. Add `InboundEmailReceipt` with scoped identity, canonical hash/version, response fields, and creation time; enforce the unique identity in SQLite. Generate `S02InboundEmailReceipts`.
4. Refactor inbound orchestration so receipt lookup, sender resolution, thread validation, creation/append, and receipt insertion occur in one short immediate write transaction for keyed messages.
5. Look up a replay before creating or touching a contact. For a new ingestion, verify an in-workspace referenced thread's inbox and actual contact email rather than treating a matching Message-ID as authorization to append anywhere.
6. Preserve shared conversation start behavior and existing reply lifecycle behavior. Stage one receipt with the final IDs and original flags; save with the message, contact changes, breach rows, and outbox rows.
7. Add scoped receipt GET, including 404 for foreign IDs. Do not use a globally unique index on Message.EmailMessageId: receipt identity deliberately includes workspace and inbox.
8. Test new-thread replay, reply replay, changed-body conflict, blank MessageId, case-sensitive identifiers, reference order, wrong sender/inbox, and another workspace using the same identifier.
9. Race equal MessageIds through separate connections and exercise a rollback after staging a new contact. Confirm no orphan contact or receipt survives the failed transaction.
10. Run email/channel/outbox regressions, migration checks, and document the new stricter thread-reference rejection behavior for relay clients.

**Concrete fixture and failure rehearsal.** `<mail-1>` opens C1; `<mail-2>` replies to C1; replaying either returns its original message ID. Sending `<mail-2>` with another body gives 409. A request referencing C1 from an unrelated sender gives 422 and creates no sender contact. The same MessageId in another legitimate inbox has independent receipt identity.

**Run:** `dotnet test --filter "FullyQualifiedName~S02|FullyQualifiedName~EmailChannelTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~WebhookOutboxTests"` plus migration checks.

**Rollout and limits.** Old messages have no receipts; this guarantee begins with new receipt-backed ingestions. Do not fabricate historical request hashes from incomplete stored fields. Keep receipts while promising replay; this slice does not purge them or parse MIME.

**Teach it back / done when.** Explain why replay identity and thread ancestry need separate checks. Complete when valid provider retries are inert and an invalid thread reference cannot attach a message to another contact's thread.

<a id="s03"></a>
## S03: Lease webhook work before sending it

**User story.** As an operator running more than one dispatcher, I want each delivery attempt to have a persisted owner before HTTP starts so workers do not normally send the same pending row at the same time.

**Learn here.** Optimistic concurrency on outcome updates happens too late to prevent two earlier HTTP calls. A lease is a temporary right to attempt work, not a guarantee about receiver-side effects.

**Open first:** [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs), [WebhookDelivery](../src/Harbor.Domain/Entities/WebhookDelivery.cs), [WebhookSender](../src/Harbor.Infrastructure/WebhookSender.cs), and [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs). Create `S03WebhookLeaseTests.cs` with separate-connection worker fixtures.

**Contract and acceptance criteria.** Keep the Admin dispatch route. Before each send, atomically claim one due Pending row for an active scoped subscription with a fresh lease token and 60-second expiry; persist the claim first. Process at most 50 claimed rows per dispatch. Existing transport timeout remains 10 seconds. Only the current unexpired lease owner may record an outcome. An expired owner must not overwrite a newer attempt. Expired leases can be reclaimed. No network operation holds a database transaction. This reduces simultaneous ownership but cannot guarantee exactly-once HTTP delivery after pauses, crashes, or lease expiry.

**Implementation plan.**

1. Draw Claim → Commit → Send → Conditional outcome commit, with a crash marker between every pair. Write expected row state at each marker.
2. Add nullable LeaseToken and LeaseExpiresAt plus a monotonically increasing ClaimGeneration to WebhookDelivery. Add an eligibility index as justified by the query; generate `S03WebhookLeases` with old rows initially unleased.
3. Extract `ClaimNextAsync(workspace, now)` into a short immediate write transaction. Select one eligible unleased/expired row, ordered CreatedAt then ID, update lease/generation/version, commit, and return detached attempt data.
4. Claim only immediately before sending that row, not an entire batch whose later leases may expire while waiting. Never share one DbContext across worker tasks.
5. Use a fresh context for completion and require matching ID, lease token/generation, and unexpired lease. Apply the existing success/backoff methods, clear the lease, and commit. A lost lease produces an internal LostOwnership outcome, not an overwrite.
6. On an expired claim with no recorded result, treat the earlier external outcome as uncertain. Reclaim with a new generation; retain existing completed AttemptCount semantics until S04 introduces an attempt ledger.
7. If cancellation occurs before sending, release the owned lease conditionally. If cancellation/crash occurs after sending may have begun, let uncertainty/recovery rules apply; do not assert that nothing was delivered.
8. Recheck subscription activity before transport when practical; document that pausing cannot recall a request already in flight. Keep its payload and delivery ID stable for receiver deduplication.
9. Test two workers competing, expiry/reclaim, stale-owner completion, inactive subscription, due-time boundary, transport timeout, and cross-workspace selection with barriers and a fake clock.
10. Run webhook/signer/concurrency regressions and migration checks. Replace misleading tests with stronger ownership-before-send assertions while retaining stale-outcome protection tests.

**Concrete fixture and failure rehearsal.** Worker A claims generation 1; B sees no available row. Advance past expiry, B claims generation 2; A's late completion updates zero rows. Crash after the receiver accepts but before completion: B may send again, so the receiver must deduplicate delivery IDs for nonrepeatable effects.

**Run:** `dotnet test --filter "FullyQualifiedName~S03|FullyQualifiedName~WebhookOutboxTests|FullyQualifiedName~ConcurrencyTests|FullyQualifiedName~HttpWebhookSenderTests"` plus migration checks.

**Rollout and limits.** Stop old dispatchers before enabling the lease-aware path; old code ignores leases. Rolling back requires pausing dispatch and letting active sends/leases settle. A lease duration is not a bound on how long a paused process can resume later.

**Teach it back / done when.** Explain the difference between persisted ownership, a recorded outcome, and an external effect. Complete when only the current owner can commit an outcome and crash tests expose, rather than conceal, possible duplicate delivery.

<a id="s04"></a>
## S04: Preserve every webhook attempt and its uncertainty

**User story.** As an Admin diagnosing a failing receiver, I want each attempted delivery's start, finish, response code, and outcome so I can distinguish repeated failures from an attempt whose result was lost.

**Dependency.** Complete S03 first. Reuse its claim generation and conditional completion; do not create a second ownership mechanism.

**Learn here.** The current delivery row describes its latest outcome, not its complete history. Recording Started before HTTP and a terminal outcome afterward makes the uncertainty between those commits visible.

**Open first:** [WebhookDelivery](../src/Harbor.Domain/Entities/WebhookDelivery.cs), [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs), [WebhooksController](../src/Harbor.Api/Controllers/WebhooksController.cs), and [WebhookDeliveryTests](../tests/Harbor.Tests/Unit/WebhookDeliveryTests.cs). Create `S04WebhookAttemptHistoryTests.cs`.

**Contract and acceptance criteria.** Add Admin-only `GET api/webhook-deliveries/{id}/attempts`, paged by attempt number ascending. Return attempt ID/number, StartedAt, FinishedAt, outcome Started/Succeeded/Rejected/TransportError/Unknown, and optional response status. A generic bounded failure code may be included; no response bodies, secrets, URLs, or raw exception text. Missing/foreign parent gives 404. Unique `(DeliveryId, ClaimGeneration)` gives one ledger entry per persisted attempt reservation. Historical pre-feature attempts remain summarized on the delivery; do not invent their missing details.

**Implementation plan.**

1. Test two rejected attempts followed by success and assert three distinct ordered ledger rows, not one row overwritten three times.
2. Add `WebhookAttempt` with delivery/workspace IDs, claim generation, attempt number, times, outcome, and status code; configure unique keys and cascade with delivery deletion. Generate `S04WebhookAttempts`.
3. During S03 claim, reserve the next attempt number and stage Started in the same transaction as the lease. Make the retry budget count reserved attempts so repeated crashes cannot bypass MaxAttempts forever.
4. Refactor `Succeed`/`Fail` bookkeeping carefully to avoid incrementing AttemptCount again at completion. Preserve established 1/2/4/8-minute backoff for recorded failures, based on the reserved attempt number.
5. Complete the ledger and delivery together only if the lease is still owned. Map HTTP non-2xx to Rejected and transport failure to TransportError; successful HTTP becomes Succeeded.
6. When recovering an expired lease, mark its still-Started attempt Unknown before deciding whether another reservation fits the budget. Unknown means the receiver may or may not have accepted; it is not an HTTP rejection.
7. At budget exhaustion, leave the delivery Failed and retain the final Unknown/failed attempt. Started reservations cancelled before transport still consume a slot in this deliberately conservative first slice; document that rule.
8. Implement the scoped paged endpoint, checking the delivery's workspace before querying children. Add a latest-attempt number to diagnostics only where it removes ambiguity.
9. Test success/failure history, interrupted completion, lease expiry, stale completion, cancellation, legacy deliveries with nonzero AttemptCount but no ledger, retry exhaustion, and foreign access.
10. Run delivery/outbox/concurrency regressions and upgrade tests; update retry documentation to identify the intentional change from completed-attempt counting to reservation counting.

**Concrete fixture and failure rehearsal.** Attempt 1 gets 500, attempt 2 loses its outcome after HTTP, and attempt 3 gets 204. History reads Rejected/Unknown/Succeeded with numbers 1/2/3. An old delivery with AttemptCount=2 starts new history at attempt 3; there are no fabricated rows 1 and 2.

**Run:** `dotnet test --filter "FullyQualifiedName~S04|FullyQualifiedName~S03|FullyQualifiedName~WebhookDeliveryTests|FullyQualifiedName~WebhookOutboxTests"` plus migration checks.

**Rollout and limits.** Deploy with the lease-aware dispatcher paused during the accounting switch. Do not mix old completion-increment logic with new reservation-increment logic. Deleting a subscription still removes its delivery history under current cascade semantics; this is operational history, not an external compliance archive.

**Teach it back / done when.** Explain why Unknown is a useful truthful outcome. Complete when attempt numbers are unique, retries respect the budget, and a crash cannot silently erase the distinction between “not accepted” and “not observed.”

<a id="s05"></a>
## S05: Manage expiring, individually revocable API keys

**User story.** As a teammate operating more than one client, I want separately named API keys with expiry and individual revocation so I can replace one client credential without replacing every client at once.

**Learn here.** The current key hash lives directly on Teammate. Moving credentials into a child table changes authentication, bootstrap creation, seeding, migration, and rollback behavior—not just a controller.

**Open first:** [ApiKeyAuthenticationHandler](../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs), [ApiKeys](../src/Harbor.Infrastructure/ApiKeys.cs), [TeammatesController](../src/Harbor.Api/Controllers/TeammatesController.cs), [WorkspacesController](../src/Harbor.Api/Controllers/WorkspacesController.cs), and [AuthorizationTests](../tests/Harbor.Tests/Integration/AuthorizationTests.cs). Create `S05CredentialLifecycleTests.cs`.

**Contract and acceptance criteria.** Add POST/GET `api/teammates/{id}/api-keys` and DELETE `api/api-keys/{id}`. A teammate manages their own keys; Admins may manage keys for teammates in their workspace. POST accepts a trimmed 1–100 character name and lifetime of 15 minutes–30 days, returning 201 with raw key once and safe metadata. GET never returns hashes/raw keys. Repeated revocation returns 204. Enforce at most five currently usable keys per teammate. At `now == ExpiresAt`, authentication fails with 401. Revoking the calling key is allowed; its next request fails. Initial legacy credentials remain nonexpiring until revoked.

**Implementation plan.**

1. Trace every `ApiKeyHash` read/write with `rg`. List the authenticator, workspace bootstrap, teammate creation, seeder, response helpers, and tests before changing the model.
2. Add `TeammateApiKey` with ID, teammate/workspace IDs, name, hash, created/expiry/revocation times, and a globally unique hash index. Generate `S05TeammateApiKeys`.
3. Backfill one credential per existing teammate from its existing hash without needing the raw key. Use deterministic backfill identity or an idempotent migration strategy; an upgrade must not invalidate existing clients.
4. Modify all initial-key creation paths to create a credential in the same transaction as the teammate. Retain the old column during this release for schema compatibility, but authentication must read the credential table only after cutover.
5. Authenticate by hash plus not revoked and not expired, joining the current teammate for role/workspace claims. Do not copy role into the key and let it become stale.
6. Implement scoped lifecycle routes. Count usable credentials and insert under a short write transaction so simultaneous creates cannot exceed five. Persist only the hash; return raw material only from the successful create response.
7. Implement revocation as a timestamp update, preserving metadata. Do not log request headers, raw secrets, or hash values, and do not put key-creation responses in generic replay receipts.
8. Test overlap during rotation, exact expiry boundary, five-key limit races, same-owner/other-owner/Admin permissions, revoked authentication, and role changes reflected on the next request.
9. Upgrade a database containing known old hashed credentials and authenticate with the existing test raw keys. Verify repeated seeding does not resurrect revoked credentials or add duplicate usable keys.
10. Run all authentication/authorization/bootstrap regressions and document create-new → move client → revoke-old as the operational rotation sequence.

**Concrete fixture and failure rehearsal.** Keys A and B both authenticate; revoke A and only B works. Crash after saving B but before returning its raw value: the raw value cannot be recovered from storage; revoke that unused credential via A and issue a new one. A role change must affect both remaining keys immediately on their next authentication.

**Run:** `dotnet test --filter "FullyQualifiedName~S05|FullyQualifiedName~Authorization|FullyQualifiedName~WorkspaceEndpointsTests|FullyQualifiedName~SeedDataSmokeTests"` plus migration checks.

**Rollout and limits.** Migrate, backfill, and switch all instances together. An old authenticator reading the legacy hash can accept a revoked key, so rolling back to it after revocations is unsafe; use a forward fix or a controlled credential recovery procedure. This slice adds no OAuth, user login, or external secret manager.

**Teach it back / done when.** Explain why credential migration needs to preserve existing authentication while preventing the old path from bypassing revocation. Complete when expiry, overlap, revocation, and legacy upgrades behave consistently.

<a id="s06"></a>
## S06: Preview and atomically merge a bounded pair of contacts

**User story.** As a workspace Admin cleaning duplicate customer records, I want to review a merge plan and commit it only if the relevant records have not changed, so one surviving contact keeps the combined support history.

**Learn here.** A contact is referenced by conversations and message authors. Correctly moving only one reference leaves an inconsistent history or a foreign-key failure. A preview is useful only if execution detects that it went stale.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs), [Contact](../src/Harbor.Domain/Entities/Contact.cs), [Message](../src/Harbor.Domain/Entities/Message.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and [ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs). Create `S06ContactMergeTests.cs`.

**Contract and acceptance criteria.** Admin POST `api/workspaces/{workspaceId}/contact-merges/preview` accepts SourceId and TargetId and returns a stored plan valid for 10 minutes. Both contacts must be distinct, in scope, and have the same nonblank normalized email; conflicting nonblank ExternalIds give 422. Limit source history to 100 conversations and 1,000 messages referencing it. Preview lists counts, field decisions, attribute conflicts, and a fingerprint. POST `api/contact-merges/{planId}/commit` executes once; first/repeated success returns 200 with target ID and original result. Stale plan gives 409; expired gives 410. The source ID becomes unavailable after success; no alias or unmerge endpoint.

**Implementation plan.**

1. Arrange source/target contacts, source conversations, contact-authored replies, and teammate notes. Write the exact target state expected after a merge before implementing any deletion.
2. Define field policy: keep target name/email; fill missing target ExternalId from source; union attributes with target values winning conflicts; keep the latest nonnull LastSeenAt. Show every conflict in the preview.
3. Add `ContactMergePlan` with owner workspace/Admin, source/target IDs, canonical plan/fingerprint, expiry, status, and committed result. Keep completed audit IDs/snapshots as historical values, not foreign keys to the deleted source. Generate `S06ContactMergePlans`.
4. Inventory every current contact reference. Fingerprint source/target scalar data and affected relationship identities/versions; include counts used in the displayed plan. Read the preview consistently so it does not mix two contact states.
5. On commit, require an Admin in the plan's workspace; another Admin in that same workspace may commit it. Record the preview creator and committing Admin separately. Return an existing committed result if present. Otherwise open an immediate write transaction and reload all scoped records and the fingerprint before mutation.
6. Reject expiry, changed fingerprints, limits, or newly unsupported dependencies before moving anything. Do not silently cascade-delete a reference introduced by another feature.
7. Repoint source conversations' ContactId and source-authored messages' AuthorContactId to target. Preserve bodies, kinds, channels, timestamps, assignments, and message IDs; apply the stated field policy to target.
8. Remove source only after every required live reference is handled. Mark the plan committed and save everything in one transaction. Immutable historical payloads/receipts retain their original IDs; they are not rewritten as though history originally named the target.
9. Test stale contact changes, new incoming source messages, unchanged teammate authors, limit rejection, foreign IDs, same-ID rejection, duplicate commit, and complete rollback on an injected write failure.
10. Run contact/email/segment regressions and migration checks. Review the before/after foreign-key graph with a second reading pass before enabling the command.

**Concrete fixture and failure rehearsal.** Source owns C1 and contact message M1; M2 is a teammate note. After merge, C1.ContactId and M1.AuthorContactId equal target; M2's author is unchanged. A new reply after preview changes the fingerprint: commit returns 409 and source still exists. Failure before commit preserves both original contacts and all original references.

**Run:** `dotnet test --filter "FullyQualifiedName~S06|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~EmailChannelTests|FullyQualifiedName~SegmentTests"` plus migration checks.

**Rollout and limits.** Ship preview before enabling commit. Source deletion is a data operation that a code rollback cannot undo; the plan/audit supports investigation, while recovery needs a deliberate data restoration procedure. Matching email is a narrow eligibility rule, not proof of human identity.

**Teach it back / done when.** Explain why both conversation ownership and historical message author references move, but message content and immutable event payloads do not. Complete when only a fresh reviewed plan can make an all-or-nothing merge.

<a id="s07"></a>
## S07: Stage a contact import and commit all valid rows together

**User story.** As a workspace Admin importing customer records, I want a row-by-row validation report before committing a batch, so I can correct errors without creating half an import.

**Learn here.** Staging separates “accepted input” from “live domain records.” A committed batch needs a durable result so a repeated commit does not insert its contacts again.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs), [ContactAttributes](../src/Harbor.Infrastructure/ContactAttributes.cs), [Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and [ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs). Create `S07ContactImportTests.cs`.

**Contract and acceptance criteria.** Admin POST `api/workspaces/{workspaceId}/contact-imports` accepts 1–200 JSON contact rows, body capped at 3 MiB. Return 201 with batch ID, Ready/Invalid status, row-numbered validation errors, and 24-hour expiry. GET `api/contact-imports/{id}` reads the scoped batch. POST `/commit` returns 200 with row-number-to-contact-ID mappings, replaying the same mapping after success. Invalid batch gives 422, expired uncommitted batch 410. This is create-only; duplicate email/ExternalId values remain allowed exactly as the current contact model allows. No CSV parsing, upsert, partial success, or background worker.

**Implementation plan.**

1. Write a mixed fixture with two valid rows and one invalid email. Staging must create zero live contacts and return the third row's error.
2. Extract reusable contact-input validation/normalization from the ordinary create path where appropriate. Validate attributes, including normalized-key collisions and final JSON size, rather than letting serialization throw during commit.
3. Add `ContactImportBatch` and ordered `ContactImportRow` records containing normalized input or safe validation details, status, timestamps, and eventual contact IDs. Use unique `(BatchId, RowNumber)` and explicit workspace ownership; generate `S07ContactImports`.
4. Validate every row rather than stopping at the first error. Preserve 1-based row order and stable field names in errors. An invalid batch is immutable: clients correct it by staging another batch.
5. Store the batch and row results, but do not attach new Contact entities yet. Hash/version the normalized representation if later code versions need to detect unsupported staged formats.
6. On commit, open an immediate write transaction, load the scoped batch, return its saved mapping if already committed, and reject Invalid/expired/unsupported input before inserting.
7. Construct contacts in row order using shared create rules, assign their generated IDs back to staged rows, mark Committed, and save once. Do not invoke the contact controller repeatedly over HTTP.
8. Add an explicit maximum serialized attribute/input size check before staging so 200 rows cannot create unbounded memory work. Keep logs to batch ID, row count, and error codes.
9. Test invalid staging, successful commit, repeated/racing commits, expiry, exact row limits, foreign batch ID, and an injected error after some contacts have been staged but before save.
10. Run contact/segment regressions, migration checks, and document retained staged input as protected customer data. This slice does not automatically purge committed mappings needed for replay.

**Concrete fixture and failure rehearsal.** Ready batch B contains rows 1/2/3; commit maps them to C1/C2/C3. Lose the response and repeat: receive C1/C2/C3 again, not three more contacts. A failed save leaves B Ready and creates zero contacts. An Invalid batch containing one good row still cannot commit that row alone.

**Run:** `dotnet test --filter "FullyQualifiedName~S07|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~SegmentSqlTranslationTests"` plus migration checks.

**Rollout and limits.** Enable staging first, then commit. Retain the ability to read committed results during rollback. Removing staged tables loses retry mappings; removing imported contacts is a separate reviewed data operation. Batch identity deduplicates commit, not two separately staged imports of identical people.

**Teach it back / done when.** Explain why validation and commit are distinct states and why an Invalid batch cannot be partially committed. Complete when each Ready batch creates its contacts at most once and returns stable row-level results.

<a id="s08"></a>
## S08: Route new conversations to teams using ordered contact rules

**User story.** As an Admin configuring support queues, I want the first matching contact rule to route a new conversation to a team, so specialized customers reach the right shared queue automatically.

**Learn here.** Both channels already share ConversationStarter. Routing should be a single ordered decision there, with existing round-robin behavior retained only as fallback. Team assignment and teammate assignment remain mutually exclusive.

**Open first:** [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs), [SegmentCompiler](../src/Harbor.Infrastructure/SegmentCompiler.cs), [AutoAssigner](../src/Harbor.Infrastructure/AutoAssigner.cs), [AssignmentEvent](../src/Harbor.Domain/Entities/AssignmentEvent.cs), and [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs). Create `S08RoutingRulesTests.cs`.

**Contract and acceptance criteria.** Admin GET/PUT `api/inboxes/{id}/routing-rules` manages a versioned ordered list of at most 20 rules, each with stable rule ID, name, optional priority/channel constraints, 1–10 existing segment conditions, and target team ID. PUT accepts ExpectedVersion: null means create only when no definition exists; otherwise require the current version. Add POST `/routing-rules/preview` for a scoped contact and proposed priority/channel. First matching usable rule wins. Invalid target teams/definitions give 422; stale configuration gives 409. Matching routing takes precedence over AutoAssign; no match keeps existing fallback. An empty rule list disables routing. No reassignment of existing conversations or arbitrary scripting.

**Implementation plan.**

1. Write a decision table: enterprise contact → specialist team; free contact → no match; two matches → earlier rule wins; no match plus AutoAssign → existing picker.
2. Add immutable `InboxRoutingVersion` definitions and an active version reference, with actor/time and canonical rule JSON. Validate all target teams in the inbox workspace. Protect configuration replacement with an immediate transaction or a conditional active-version update, not only a preceding C# comparison. Generate `S08InboxRouting`.
3. Use `SegmentCompiler` to validate rules and evaluate an existing contact in SQL. For a newly staged inbound contact that has not been saved yet, first persist that contact inside the caller's still-uncommitted transaction before querying it; do not run SQL-only JsonExtract as a .NET delegate.
4. Establish transaction ownership around both chat and inbound start paths so this intermediate save cannot commit a contact without its conversation. Keep final creation/outbox commit atomic, including S02 receipts if that feature exists.
5. Implement a shared decision service returning matched rule/version/team or NoMatch. Preview uses it without changing the inbox pointer, creating messages, or saving a conversation.
6. Invoke the decision before AutoAssigner in ConversationStarter. For a match, call `AssignToTeam`, stage an assignment event with a new documented routing kind and routing version/rule metadata, and queue ConversationAssigned for team assignments too.
7. Preserve existing numeric enum values and webhook wire names when extending metadata. If a target team was deleted after rule publication, skip that unusable rule and expose a reason in preview/diagnostics rather than failing all inbound conversations.
8. Store the selected routing version/rule on assignment history as historical IDs so later edits do not rewrite the decision explanation. Configuration replacement never reroutes old threads.
9. Test chat/new-email parity, staged new contacts, first-match precedence, unavailable targets, stale rule updates, no-match fallback, no pointer movement on a team match, and foreign rule/contact/inbox references.
10. Run assignment/channel/segment/outbox regressions and migration checks. Use a preview-only stage before enabling routing on an inbox.

**Concrete fixture and failure rehearsal.** Two rules both match `plan=enterprise`; the first points to Team A and the second to B. New chat and new email both reach A. Remove A, and the next preview/creation uses B. Crash after the new email contact's intermediate save but before outer commit: neither contact nor conversation survives.

**Run:** `dotnet test --filter "FullyQualifiedName~S08|FullyQualifiedName~AssignmentRulesTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~SegmentSqlTranslationTests"` plus migration checks.

**Rollout and limits.** Migrate and ship disabled definitions first; enable one inbox after preview verification. Disabling rules affects future starts only. Rule evaluation is bounded, but 20 rules still have measurable query cost; record it rather than claiming constant-time routing.

**Teach it back / done when.** Explain why a new unsaved contact cannot be evaluated by a SQL query yet, and why saving it requires an outer transaction. Complete when both channels use one explainable decision with atomic creation effects.

<a id="s09"></a>
## S09: Watch conversations and receive private reply notifications

**User story.** As a teammate following an investigation, I want an in-app notification when the customer replies to a watched conversation so I can return to it without changing its assignee.

**Learn here.** Watching is a personal relationship; a notification is a persisted consequence of a message. Both differ from assignment, bookmarks, and an external webhook subscription.

**Open first:** [ConversationsController.AddMessage](../src/Harbor.Api/Controllers/ConversationsController.cs), [EmailController](../src/Harbor.Api/Controllers/EmailController.cs), [Conversation.RegisterMessage](../src/Harbor.Domain/Entities/Conversation.cs), and [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs). Create `S09WatchedReplyTests.cs`.

**Contract and acceptance criteria.** Both roles may PUT/DELETE `api/conversations/{id}/watch` for themselves, idempotently returning 204 after scoped parent checks. Limit a conversation to 200 watchers. GET `api/workspaces/{workspaceId}/my-notifications?unreadOnly=true` returns personal paged notifications newest first, with ID, conversation/message IDs, and creation/read timestamps. PUT `api/notifications/{id}/read` returns 204, including repeats. Another owner's notification gives 404. Generate notifications only for new Contact/Reply messages appended to existing threads, including inbound email. No notifications for notes, teammate replies, opening messages, or another tenant; no email/push delivery.

**Implementation plan.**

1. Arrange two watchers and one nonwatcher. Add a customer reply and assert exactly one notification per watcher, with no assignment changes.
2. Add `ConversationWatcher` with unique `(ConversationId, TeammateId)` and `ReplyNotification` with unique `(MessageId, TeammateId)`, ownership indexes, and explicit parent deletion behavior. Generate `S09ReplyNotifications`.
3. Implement scoped watch/unwatch. Serialize new-watch count+insert under a short write transaction to enforce the 200-person limit; a duplicate self-watch does not consume another slot.
4. Extract `StageReplyNotificationsAsync` that selects the current committed watcher set for the scoped thread and stages rows without saving. Define recipients as that observed set; do not promise notifications for someone who starts watching after it is read.
5. Call the helper from both existing-thread contact reply paths: AddMessage and EmailController.Inbound. If a shared message helper already exists, place the integration there and ensure neither caller invokes it twice.
6. Save notifications with their message and other effects. Retried database work must use the same staged message identity within its attempt; provider/client replay across requests remains governed by S01/S02 only if those separate features exist.
7. Implement owner-filtered listing and mark-read. Mark-read sets a timestamp once; repeated calls preserve that first read time. Map metadata without copying the customer body into every notification row.
8. Unwatch affects future fan-out, not existing notification history. Add no public observer API or Admin bypass to another person's personal notification list.
9. Test notes/teammate exclusion, chat/email parity, watcher limits, duplicate watch, both owners in one tenant, foreign parent, stable paging, and rollback of notification plus message after an injected concurrency failure.
10. Run messaging/channel/outbox/authorization regressions and migration checks. Add a manual walkthrough: watch, customer reply, list unread, mark read, list unread again.

**Concrete fixture and failure rehearsal.** A and B watch C; customer message M creates N(A,M) and N(B,M). A reads theirs, leaving B unread. A later teammate note creates zero notifications. If the message save fails, neither M nor either notification exists. Unwatching A leaves A's old notification available but prevents the next one.

**Run:** `dotnet test --filter "FullyQualifiedName~S09|FullyQualifiedName~MessagingEndpointsTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~AuthorizationTests"` plus migration checks.

**Rollout and limits.** Enable fan-out only after readers and schema exist. Disabling creation can stop new notifications while retaining existing personal history. The cap bounds synchronous fan-out; this does not provide external delivery, guaranteed human attention, or historical backfill for old replies.

**Teach it back / done when.** Explain why a notification should disappear when its message transaction rolls back. Complete when recipients and ownership are correct across both channels and marking one person's notification read affects nobody else.

<a id="s10"></a>
## S10: Schedule a draft article for publication

**User story.** As a help-center Admin, I want to schedule a draft for a future UTC time and see whether it published or needs review, so planned content can go live without an Admin being online at that moment.

**Learn here.** A durable schedule survives a restart; a timer only wakes the process to inspect it. The worker must also detect that the article changed after scheduling.

**Open first:** [HelpCenterController](../src/Harbor.Api/Controllers/HelpCenterController.cs), [Article](../src/Harbor.Domain/Entities/Article.cs), [Program](../src/Harbor.Api/Program.cs), and [HelpCenterTests](../tests/Harbor.Tests/Integration/HelpCenterTests.cs). Create `S10ScheduledPublicationTests.cs`.

**Contract and acceptance criteria.** Admin GET/PUT/DELETE `api/articles/{id}/publication-schedule` reads, creates/replaces, or cancels the article's current schedule. GET returns current article version and nullable schedule. PUT requires ExpectedArticleVersion and PublishAt strictly in the future, at most 90 days away; article must be Draft. Return 200; stale version gives 409, invalid timing/state 422. Persist Pending/Completed/Cancelled/NeedsReview status. A worker checks every 30 seconds when explicitly enabled and publishes up to 20 due schedules per pass. Edits/manual publish/unpublish invalidate or cancel a pending schedule. No promise of exact-second publication.

**Implementation plan.**

1. Write a fixed-clock test scheduling a draft at 12:00: before 12:00 it is private; at the first due pass it becomes public once.
2. Add an article concurrency token if absent, plus `ArticlePublicationSchedule` with unique ArticleId, workspace, author, expected article version, time, status/reason, and its own concurrency token. Generate `S10PublicationSchedules`, backfilling a nonempty article token.
3. Extract a publication service used by manual and scheduled publication. Preserve first PublishedAt semantics. If S17 exists, this same service must publish its snapshot rather than bypassing it.
4. Implement scheduling with current-version and Draft checks. Replacing a schedule requires current article data; do not accept a version from another article. Cancellation is idempotent after verifying the scoped parent.
5. Integrate ordinary content edits as NeedsReview and manual publish/unpublish as Cancelled for pending schedules in the same save. Keep the schedule's original intended time visible for diagnosis.
6. Build `ProcessDueAsync(now, limit)` with a fresh context/short transaction per schedule. Recheck Pending, due time, article version/state, and that the scheduling teammate still exists as an Admin in the workspace.
7. On a changed article or lost author permission, persist NeedsReview without publishing. On a valid schedule, publish and mark Completed in one commit; concurrent processors rely on both tokens so only one succeeds.
8. Add a hosted worker with explicit enable/poll configuration, cancellation support, scoped services per pass, and bounded error logging. Keep tests manually invoking the processing service instead of waiting 30 seconds.
9. Test process restart, equal due boundary, edit-after-schedule, manual cancellation, duplicate processors, removed/demoted author, foreign IDs, and rollback between staged publication and completion.
10. Run public/help-center/concurrency regressions and upgrade tests, then document worker enablement and the meaning of NeedsReview.

**Concrete fixture and failure rehearsal.** Draft V1 is scheduled; an edit makes V2, and the schedule becomes NeedsReview. It must not publish V2 automatically under V1's approval. Crash before the publish commit: still Pending/private. Crash after commit: Completed/public, and the next pass makes no second transition.

**Run:** `dotnet test --filter "FullyQualifiedName~S10|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~ConcurrencyTests"` plus migration checks.

**Rollout and limits.** Deploy schedule APIs with the worker disabled, verify a disposable due job, then enable processing. Disable the worker before a rollback; already published content does not become private by rolling back code. Scheduling uses UTC and the next successful poll, not a real-time guarantee.

**Teach it back / done when.** Explain why the schedule stores an article version and why a timer alone is not a durable scheduler. Complete when restarts, edits, and competing passes cannot silently publish an unreviewed draft.

<a id="s11"></a>
## S11: Support SLA deadlines measured in UTC working hours

**User story.** As an Admin whose team has defined working hours, I want SLA targets to count only configured working time so nights and configured closure dates do not consume the team's response allowance.

**Learn here.** Deadline calculation is business logic with boundary rules. Calendar definitions should be immutable once referenced so a configuration edit does not silently rewrite the explanation for an existing target.

**Open first:** [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), [SlaPolicy](../src/Harbor.Domain/Entities/SlaPolicy.cs), [Conversation.ApplySlaTargets](../src/Harbor.Domain/Entities/Conversation.cs), [SlaBoundaryTests](../tests/Harbor.Tests/Unit/SlaBoundaryTests.cs), and [SlaPolicyResolutionTests](../tests/Harbor.Tests/Unit/SlaPolicyResolutionTests.cs). Create `S11WorkingHourSlaTests.cs` and pure calendar tests.

**Contract and acceptance criteria.** Admin POST/GET `api/workspaces/{workspaceId}/working-calendars` creates/lists immutable calendar versions. Define UTC weekday intervals, at most four per day, and up to 100 closed UTC dates. Reject overlapping/empty intervals and a week with no working time. SLA policy create/update may reference a scoped calendar version. With no calendar, preserve elapsed-minute behavior. With one, calculate both targets by consuming working duration from CreatedAt, retaining subminute precision. Stamp the applied calendar version on the conversation. Limit calculation to 366 days; an unreachable target gives 422 rather than an unbounded loop. Local time zones/DST are explicitly outside this first slice.

**Implementation plan.**

1. Write a table of calendar boundaries: inside a window, before opening, exactly at closing, weekend crossing, closed date, and a target that ends exactly at closing.
2. Add immutable calendar/version storage with canonical interval JSON, workspace, name, created time, and creator. Add optional references on policies and applied-target metadata on conversations; generate `S11WorkingCalendars`.
3. Validate and normalize intervals into ordered half-open UTC windows. Model a closed date as removing that date's windows; reject malformed dates and excessive input before saving.
4. Implement a pure `AddWorkingDuration(start, minutes, calendar)` that jumps between windows, consumes ticks accurately, and stops at the horizon. Handle date arithmetic overflow as a business error.
5. Extract target calculation from assignment to the entity so existing elapsed targets and working-hour targets share the same caller. Use current `SlaPolicies.Resolve` precedence unchanged.
6. During start/priority changes, select the current policy's referenced immutable calendar, calculate deadlines from original CreatedAt, and stamp its version. Replacing a policy's calendar affects future applications, not every existing conversation automatically.
7. Preserve milestone/breach comparisons: a response exactly at its due time is not late, and first resolution remains recorded after reopening. Do not pause or recalculate clocks merely because a thread snoozes.
8. Add scoped calendar/policy contracts and clear UTC labels to responses/docs. Refuse a foreign calendar reference with 422. Do not allow updating a calendar row already used by conversations; create another version instead.
9. Test chat/email parity, fallback policies, fractional-minute starts, holidays, weekend wrapping, horizon overflow, immutable old versions, and policy changes that leave existing due times alone.
10. Run SLA/policy/channel regressions and migration checks. If S12 exists, make its preview and commit call this same calculator.

**Concrete fixture and failure rehearsal.** Monday–Friday 09:00–17:00 UTC: Friday 16:30 plus 60 working minutes ends Monday 09:30. Closing Monday shifts that result to Tuesday 09:30. Friday 16:30 plus 30 ends exactly Friday 17:00. A calendar that cannot fit the requested duration within 366 days rejects creation atomically, including its opening message/outbox.

**Run:** `dotnet test --filter "FullyQualifiedName~S11|FullyQualifiedName~SlaBoundaryTests|FullyQualifiedName~SlaPolicyResolutionTests|FullyQualifiedName~ChannelParityTests"` plus migration checks.

**Rollout and limits.** Ship the new calculator with all existing policies retaining null calendar references. Enable selected policies after example verification. Disabling new calendar selection does not erase stamped deadlines; older code must not recalculate calendar-backed policies as elapsed minutes during rollback.

**Teach it back / done when.** Explain the difference between a target's duration, its calendar version, and its computed deadline. Complete when all three are reproducible and existing elapsed-time SLA behavior remains intact.

<a id="s12"></a>
## S12: Preview and reapply current SLA policy settings to selected conversations

**User story.** As an Admin correcting SLA configuration, I want to review how current policies would change selected unfinished conversations and apply that exact plan without changing their priorities.

**Learn here.** Updating policy configuration and migrating existing records are separate operations. A preview must bind to both conversation state and the policy inputs used to calculate it.

**Open first:** [SlaPoliciesController](../src/Harbor.Api/Controllers/SlaPoliciesController.cs), [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), [SlaBreaches](../src/Harbor.Infrastructure/SlaBreaches.cs), [Conversation](../src/Harbor.Domain/Entities/Conversation.cs), and [SlaEngineTests](../tests/Harbor.Tests/Integration/SlaEngineTests.cs). Create `S12SlaReapplyTests.cs`.

**Contract and acceptance criteria.** Admin POST `api/workspaces/{workspaceId}/sla/recalculations/preview` accepts 1–100 distinct conversation IDs, all Open/Snoozed and in scope. Return a stored five-minute plan with old/new due times, old/new policy IDs, unchanged priorities, and input fingerprint. POST `api/sla-recalculations/{id}/commit` returns 200 with the applied result, replaying that result after success. Missing related selections give generic 422; stale inputs give 409; expired uncommitted plan gives 410. Commit changes targets/policy metadata and UpdatedAt only, then detects breaches at execution time. Existing breach history is retained. No bulk priority change or automatic migration of the whole workspace.

**Implementation plan.**

1. Arrange two existing conversations, change a policy afterward, and show that normal reads preserve old targets while preview proposes the newly calculated targets.
2. Add `SlaRecalculationPlan` with workspace/Admin ownership, ordered items, old conversation versions, canonical policy/inbox fingerprints, creation/expiry, and committed result. Generate `S12SlaRecalculations`.
3. Reuse the real resolver/calculator for each selected conversation at its existing priority and CreatedAt. Include inbox fallback minutes and every field used for policy precedence in the canonical configuration fingerprint.
4. If S11 is installed, include the selected immutable calendar version and use its calculator. Do not implement a second simplified formula in preview.
5. Read preview inputs consistently and enforce list size/distinctness/state before saving a plan. Clearly label any predicted breach observation with preview time; due times are fixed by the plan, but breach status can change as time passes.
6. On commit, return an existing committed result first. Otherwise open an immediate write transaction, reload all scoped selections/configuration, and compare versions/fingerprints before mutation.
7. Reject any mismatch rather than silently regenerating a different plan. Apply the reviewed targets, touch UpdatedAt, preserve priority, CreatedAt, LastMessageAt, assignment, and first milestones.
8. Run `SlaBreaches.DetectAsync` using one execution time, stage any new unique breach events, mark the plan committed, and save together. Do not delete an old breach merely because a deadline became later.
9. Test changed policy, changed inbox fallback, concurrent reply/state change, expiry, repeat commit, an unavailable last ID, no-op targets, and injected failure after one item is staged.
10. Run SLA/report/concurrency regressions and migration checks. Document that this explicit migration can make old work immediately overdue without restarting its clock.

**Concrete fixture and failure rehearsal.** C1 created at 10:00 currently has a 12:00 due time. New policy allows 30 minutes; preview proposes 10:30, not commit-time plus 30. A teammate reply after preview invalidates the conversation version and causes 409. If the plan is fresh, all selected targets and the completed plan record commit together or none do.

**Run:** `dotnet test --filter "FullyQualifiedName~S12|FullyQualifiedName~SlaEngineTests|FullyQualifiedName~SlaEngineEdgeCaseTests|FullyQualifiedName~ReportingTests"` plus migration checks.

**Rollout and limits.** Release preview before commit, and start with a small selection. Rolling back code does not restore previous deadlines or erase recorded breaches; retain the before/after plan for a deliberate corrective operation. A preview does not freeze the wall clock.

**Teach it back / done when.** Explain why unchanged conversation versions alone are insufficient when policies can change. Complete when only fresh configuration-and-conversation plans apply, with all target changes committed atomically.

<a id="s13"></a>
## S13: Attach small files to existing chat conversations

**User story.** As a teammate discussing a support issue, I want to upload a small file and attach it to a chat reply or note, with downloads restricted to people who can read that conversation.

**Learn here.** A filesystem rename and a SQL commit are different operations. Model staged uploads explicitly so an interruption cannot advertise a nonexistent attachment or make an unattached file public.

**Open first:** [ConversationsController.AddMessage](../src/Harbor.Api/Controllers/ConversationsController.cs), [Message](../src/Harbor.Domain/Entities/Message.cs), [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs), [Program](../src/Harbor.Api/Program.cs), and [MessagingEndpointsTests](../tests/Harbor.Tests/Integration/MessagingEndpointsTests.cs). Create `S13AttachmentTests.cs` with a disposable storage directory.

**Contract and acceptance criteria.** Both roles may POST one multipart file to `api/conversations/{id}/attachments` for a scoped Chat conversation, maximum 5 MiB, returning 201 staged metadata. Add up to three distinct AttachmentIds to AddMessageRequest; IDs must be unexpired, unattached, owned by the caller, and staged for that thread. Email conversations reject attachment use with 422. GET `api/attachments/{id}/content` authorizes through the attached message's conversation, or the uploader for a staged file. Serve downloads as `application/octet-stream` with attachment disposition and a safe display filename. Staged files expire after 24 hours. No opening-message attachments, inline rendering, external object store, or SMTP attachments.

**Implementation plan.**

1. Draw Uploaded → Attached or Deleting → Deleted. Write the difference between an existing private blob and an attachment visible in a saved message.
2. Add `MessageAttachment` metadata: workspace, intended conversation, uploader, optional message, original safe display name, byte length, content hash, storage ID, state, expiry, and concurrency token. Generate `S13MessageAttachments`.
3. Configure a private storage root outside the web root. Derive paths from server-generated IDs only; resolve and verify containment. Strip path separators/control characters from the display filename, but never use it as the storage path.
4. Stream to a temporary file with an enforced byte counter; reject over-limit uploads with 413 and clean up that attempt. Hash bytes while streaming, rename to its immutable storage ID, then insert Uploaded metadata. Do not hold a SQL write transaction during upload.
5. Add Admin `POST api/workspaces/{workspaceId}/attachments/reconcile`, processing at most 100 artifacts per call in that workspace's storage directory. Mark expired staging rows Deleting before removing their files; retry interrupted Deleting rows. Remove unreferenced blobs only after 24 hours. Verify path containment and never delete a referenced Attached file. This handles a crash between file creation and metadata commit.
6. Extend message validation to load all requested metadata under owner/workspace/thread scope and verify files exist. Mark them Attached with the message in the same database commit; tokens prevent two messages from claiming the same upload.
7. Extend safe message mappings/read queries to include attachment metadata without exposing storage paths. Keep ordinary messages and existing webhook payload behavior compatible except for the documented additive metadata field.
8. Implement authorized download and uploader-only `DELETE api/attachments/{id}` for staged files, using the Deleting lifecycle and returning 204. Attached files reject deletion with 409; missing metadata gives 404. An authorized download with a missing blob gives a clear 503 and an operator diagnostic, never an empty successful file.
9. Test size/count limits, malicious-looking filenames, another uploader, another workspace, wrong conversation, expired staging, concurrent attachment claims, email rejection, and unauthorized downloads.
10. Rehearse orphan-file and missing-file recovery; run message/authorization/channel regressions, migration checks, and storage cleanup tests using only disposable directories.

**Concrete fixture and failure rehearsal.** Upload U belongs to A/C1. Attaching it to C2 or as B gives 422. A successfully attaches it to M1; a second claim cannot attach U to M2. Crash after blob rename but before metadata commit leaves a private orphan that reconciliation later removes. Failed message commit leaves U staged, not falsely attached.

**Run:** `dotnet test --filter "FullyQualifiedName~S13|FullyQualifiedName~MessagingEndpointsTests|FullyQualifiedName~AuthorizationTests|FullyQualifiedName~ChannelParityTests"` plus migration/storage checks.

**Rollout and limits.** Provision private durable storage before enabling upload. This first storage adapter assumes instances sharing the database also share the configured blob storage; do not deploy independent local directories behind one API without addressing that. Rolling back must preserve existing blob and metadata readers. Downloads are untrusted files, not executable or inline application content.

**Teach it back / done when.** Explain which failures SQL can roll back and which require file reconciliation. Complete when attachment ownership is enforced and interrupted writes leave recoverable, private artifacts.

<a id="s14"></a>
## S14: Resume a durable workspace event stream

**User story.** As a client showing live support activity, I want to reconnect after a network interruption and resume committed events so I do not have to guess which updates I missed.

**Learn here.** Server-sent events are transport; a database event log provides replay. A stream cannot replay facts that were only emitted from process memory before a restart.

**Open first:** [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs), [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), [EmailController](../src/Harbor.Api/Controllers/EmailController.cs), [Webhooks](../src/Harbor.Infrastructure/Webhooks.cs), and [WorkspaceScopeMatrixTests](../tests/Harbor.Tests/Integration/WorkspaceScopeMatrixTests.cs). Create `S14WorkspaceEventStreamTests.cs`.

**Contract and acceptance criteria.** Both roles may GET `api/workspaces/{workspaceId}/events` as authenticated `text/event-stream`. Support ConversationCreated, MessageCreated, StateChanged, and AssignmentChanged only. Persist metadata identifying the entity/change, not full message bodies. Event IDs are protected resume tokens bound to workspace, sequence, format version, and seven-day expiry; accept Last-Event-ID on reconnect. With no cursor, start at the current high-water mark and send a ready token. Valid replay returns later events in sequence order. Wrong-workspace/malformed token gives 400; expired or purged history gives 410 with a refresh instruction. Heartbeats carry no event ID. No priority/tag event coverage in this slice.

**Implementation plan.**

1. Draw write → SQL commit → stream poll → client acknowledgement. Specify that clients deduplicate by event ID and can refresh ordinary APIs after a 410.
2. Add `WorkspaceEvent` with database-generated increasing integer sequence, workspace, event type/version, entity/conversation IDs, bounded metadata JSON, and recorded time. Add a per-workspace purged-through watermark; generate `S14WorkspaceEvents`.
3. Stage event rows alongside existing writes. Creation records both the conversation and opening-message events; appended messages record MessageCreated and, when applicable, the resulting state change. Assignment/state endpoints record actual transitions, not no-ops.
4. Use one staging helper from chat/email/shared start paths. Save the event log in the same transaction as domain effects. This event feed is separate from webhook subscriptions and must not depend on one being configured.
5. Protect/validate cursor payloads with a maintained application token-protection facility and persisted shared keys. Do not encode plain sequence IDs as if base64 were authentication. Test format/version/workspace binding.
6. Implement short scoped database polls of at most 100 rows, ordered by sequence. Release each context/connection between polls; write and flush SSE frames, respect cancellation, and emit a heartbeat during inactivity.
7. Refresh resume-token expiry when emitting a new event or initial ready token. Document at-least-once replay: disconnecting after receipt but before the client saves its cursor can replay the last event.
8. Add Admin `POST api/workspaces/{workspaceId}/events/prune` to remove at most 1,000 events from an older-than-seven-days sequence prefix and advance the watermark atomically. Never advance past retained earlier events or assume global sequence numbers are gapless per workspace.
9. Test reconnect ordering, equal timestamps, multiple tenants with interleaved global sequences, tampered cursors, expiry/pruning, cancellation, restart with shared protection keys, and a rolled-back domain write producing no event.
10. Run state/channel/authorization regressions and migration checks. Use a streaming HttpClient test with bounded cancellation, not a test that waits indefinitely for an open stream to finish.

**Concrete fixture and failure rehearsal.** A receives events E10/E14 in its workspace while another tenant owns E11–E13. Reconnect after E10 returns E14, with no foreign payloads. Crash after commit but before streaming E14: replay still finds it. A purged cursor gives 410, not a silently incomplete success stream.

**Run:** `dotnet test --filter "FullyQualifiedName~S14|FullyQualifiedName~ConversationStateMachineTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~WorkspaceScopeMatrixTests"` plus migration checks.

**Rollout and limits.** Enable durable writes before advertising the stream, and persist protection keys before issuing reusable tokens. Old data has no backfilled events. Limit stream connection lifetime to five minutes and reconnect so long-lived requests periodically reauthenticate; key revocation is not instantaneous inside an already authorized stream.

**Teach it back / done when.** Explain why stream ordering uses sequence rather than timestamps and why expired history must be explicit. Complete when committed events survive disconnect/restart and replay cannot cross workspace boundaries.

<a id="s15"></a>
## S15: Generate a consistent conversation export as a durable job

**User story.** As an Admin reviewing support records offline, I want to request a bounded conversation-summary export and download it when ready, without holding the initiating HTTP request open for the whole export.

**Learn here.** Job persistence, snapshot consistency, file publication, and job ownership are separate concerns. A stable file must not be exposed merely because its first bytes were written.

**Open first:** [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs), [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs), [Program](../src/Harbor.Api/Program.cs), [HarborApiFactory](../tests/Harbor.Tests/Integration/HarborApiFactory.cs), and [ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs). Create `S15ConversationExportTests.cs`.

**Contract and acceptance criteria.** Admin POST `api/workspaces/{workspaceId}/conversation-exports` accepts supported conversation filter criteria, excluding paging, and returns 202 with a job Location. GET `api/conversation-exports/{id}` returns Queued/Running/Ready/Failed/Expired status and safe metadata; GET `/download` returns the Ready JSON Lines file or 409 when not ready, 410 when expired. Restrict access to workspace Admins. Export at most 10,000 summaries and 50 MiB, ordered CreatedAt then ID, without message bodies. Snapshot is taken when processing begins, not when queued. Ready files expire after 24 hours. No whole-database backup or guaranteed historical export at enqueue time.

**Implementation plan.**

1. Specify the first JSONL record as a versioned manifest with snapshot time, filter description, and row count, followed by existing safe summary records. Write a three-conversation golden fixture.
2. Add `ConversationExportJob` with scoped requester, frozen filter definition/version, state, lease/generation, attempt count, snapshot time, counts/hash, private artifact ID, expiry, and safe failure code. Generate `S15ConversationExports`.
3. Validate references at enqueue. If filtering by a segment, capture its rule definition rather than only an ID whose rules could change while queued. Evaluate contact data at snapshot time using that captured definition.
4. Create an explicitly enabled scoped worker. Claim a queued/expired-running job in a short write transaction with a five-minute lease, permit at most two processing attempts, and give each generation a unique temporary artifact path.
5. Open a separate read transaction for the export snapshot, establish the database snapshot, and record its time. Reuse scoped query criteria, applying captured segment rules if needed. Count with a limit guard before writing data.
6. Read summaries in bounded pages within that same snapshot and write a private temporary file. Enforce byte/time bounds, with a two-minute processing budget. Do not hold a write reservation while exporting; measure read-snapshot effects under the configured SQLite journal mode.
7. Finish and hash the file, rename to an immutable artifact ID, then conditionally mark Ready only if the job generation/lease still belongs to this worker. A stale worker's file is an orphan, never the downloadable result.
8. Implement scoped status/download and expiry cleanup. Download reads only a completed artifact; paths are server-derived and private. Cleanup can retry interrupted removal and must not remove another generation's active output.
9. Test mutation during export with separate connections to prove internally consistent membership/tags, competing workers, limit failures, crash before/after rename, expired download, foreign jobs, and cancellation.
10. Run queue/authorization/migration tests and a disposable multi-connection export rehearsal. Record processing bounds and cleanup behavior in operational docs.

**Concrete fixture and failure rehearsal.** Start snapshot with C1/C2/C3. A concurrent change after the snapshot does not make page two include a different logical dataset. Crash after file rename but before Ready: no download is exposed; recovery can rebuild and later remove the orphan. Two worker generations cannot both publish their artifacts as the job's result.

**Run:** `dotnet test --filter "FullyQualifiedName~S15|FullyQualifiedName~ConversationQueueTests|FullyQualifiedName~WorkspaceScopeMatrixTests"` plus migration/file/connection checks.

**Rollout and limits.** Provision private durable shared storage, deploy APIs with the worker disabled, test one job, then enable it. Disabling processing does not delete Ready artifacts. Rolling back must retain a reader or an explicit download-expiry plan. This bounded export may block or contend with writes depending on SQLite configuration; measure that cost.

**Teach it back / done when.** Explain why several individually correct paged queries are not necessarily one consistent snapshot. Complete when a Ready artifact represents one scoped snapshot and interrupted workers cannot publish partial files.

<a id="s16"></a>
## S16: Audit changes to support configuration

**User story.** As a workspace Admin investigating a configuration change, I want to see who changed teammate availability/capacity, team membership, or canned replies and when, so I can explain unexpected support behavior.

**Learn here.** An audit record describes a committed mutation and its authenticated actor. It must not be a free-form log of secrets or an entry saved even when the actual change failed.

**Open first:** [TeammatesController](../src/Harbor.Api/Controllers/TeammatesController.cs), [TeamsController](../src/Harbor.Api/Controllers/TeamsController.cs), [CannedRepliesController](../src/Harbor.Api/Controllers/CannedRepliesController.cs), [RequestLoggingMiddleware](../src/Harbor.Api/Infrastructure/RequestLoggingMiddleware.cs), and [TeamAndTeammateEndpointsTests](../tests/Harbor.Tests/Integration/TeamAndTeammateEndpointsTests.cs). Create `S16ConfigurationAuditTests.cs`.

**Contract and acceptance criteria.** Admin GET `api/workspaces/{workspaceId}/configuration-audit` supports actor ID, action, entity kind/ID, inclusive From, exclusive Before, and normal paging. Return event ID, actor ID, action, entity IDs, changed-field names, time, and allowlisted old/new availability/capacity or membership IDs. For canned replies, record changed fields and identity, not old/new title/body/shortcut content. Include create/update/delete and actual membership/availability changes; no-op writes add nothing. Sort timestamp descending then ID. Invalid enum/range filters give 400. No public access, external audit sink, arbitrary request-body storage, or historical reconstruction of pre-feature changes.

**Implementation plan.**

1. Enumerate the specific mutation actions in the three controllers. Write a table of each action's expected audit kind, target IDs, and allowed fields.
2. Add `ConfigurationAuditEvent` with workspace, raw actor/target IDs, action/entity kind, occurred time, schema version, and bounded allowlisted change data. Index workspace/time/ID and common filter keys; generate `S16ConfigurationAudit`.
3. Keep actor/entity references as historical values so deleting a team or canned reply does not erase the audit. Cascade only with workspace deletion, matching the intended retention boundary.
4. Build a small typed audit factory or explicit per-feature helpers. Do not reflectively serialize entire entities: teammates contain key hashes and canned replies contain text that the audit contract excludes.
5. Capture before values after scope/permission checks but before mutation. Compare normalized actual values to detect no-ops, including repeated add-member operations and unchanged availability.
6. Stage the event with its domain mutation and save once. An Agent changing their own availability is a valid audited actor even though only Admins can read the audit list.
7. Add scoped SQL filtering and stable paging to the reader. Resolve current display names only as optional decorations; historical actor IDs remain authoritative if the person is gone or renamed.
8. Add a test that deliberately changes a secret-like field/body and verifies it never appears in stored audit JSON or response JSON. Store fixed action identifiers rather than user-authored descriptive sentences.
9. Test all action types, no-ops, authorization failures, duplicate membership failures, rollback, deleted targets, date boundaries, foreign workspace, and an invalid filter value.
10. Run teammate/team/canned-reply/authorization regressions and migration checks. Document the exact covered actions so operators do not assume every application write is audited.

**Concrete fixture and failure rehearsal.** Agent A changes capacity 2→4 and Availability Available→Away: one event lists both fields and their allowed values. Updating a canned reply body records `Body` in changed fields but never its text. A failed save creates neither the new configuration nor an audit event. Removing the canned reply later leaves its earlier audit IDs intact.

**Run:** `dotnet test --filter "FullyQualifiedName~S16|FullyQualifiedName~TeamAndTeammateEndpointsTests|FullyQualifiedName~TagAndCannedReplyEndpointsTests|FullyQualifiedName~AuthorizationTests"` plus migration checks.

**Rollout and limits.** Migrate before enabling writers. Preserve readers during rollback so already-recorded events remain inspectable. This is an application audit with explicit coverage and workspace-lifetime retention, not a tamper-proof archive or a substitute for database backups.

**Teach it back / done when.** Explain why a generic whole-entity serializer is dangerous here and why a no-op need not become a change event. Complete when each covered committed change has one safe, correctly attributed audit entry.

<a id="s17"></a>
## S17: Keep draft edits separate from a published article

**User story.** As a help-center Admin improving a live article, I want edits to remain private until I publish them so readers continue seeing the last approved content while I work.

**Learn here.** The current Article row supplies both authoring and public content, so editing a Published article changes the live page immediately. Separating editorial state from published content changes every public read path, not only the publish action.

**Open first:** [HelpCenterController](../src/Harbor.Api/Controllers/HelpCenterController.cs), [PublicHelpCenterController](../src/Harbor.Api/Controllers/PublicHelpCenterController.cs), [Article](../src/Harbor.Domain/Entities/Article.cs), [ArticleSuggestions](../src/Harbor.Domain/ArticleSuggestions.cs), and [HelpCenterTests](../tests/Harbor.Tests/Integration/HelpCenterTests.cs). Create `S17PublicationSnapshotTests.cs`.

**Contract and acceptance criteria.** Keep ordinary article edits as draft/editorial content. Publish atomically creates an immutable content snapshot and makes it the article's active public version; unpublish removes its active public route. Public title/body/slug/collection come only from that snapshot. Internal article responses add HasUnpublishedChanges while retaining editorial fields. A draft slug change does not break the old public URL until publication. Enforce unique active public slugs per workspace independently of draft slug uniqueness. Existing PublishedAt remains first publication time. No editorial branching, approval roles, preview-sharing URLs, or automatic publication of edits.

**Implementation plan.**

1. Write the central test: publish body A, edit body B, read internal B and public A, publish again, then read public B. Repeat with a slug and collection change.
2. Add `ArticlePublicationSnapshot` containing immutable title/body/slug/collection values, publish time, and article/workspace IDs. Add `ActiveArticlePublication` with unique ArticleId and unique `(WorkspaceId, Slug)` pointing to a snapshot. Add article concurrency protection if absent; generate `S17ArticlePublications`.
3. Backfill one snapshot and active route for every existing Published article, preserving its current public content and first-publication timestamp. Drafts get no route. Test upgrade before changing public readers.
4. Build a publication service: validate the editorial collection and active-slug availability, stage a new snapshot, replace the active route, call existing publication lifecycle behavior, and commit together. Repeated publish with identical content may be a no-op rather than creating duplicate snapshots.
5. Make unpublish remove the active route and use the existing lifecycle method in one transaction. Keep old snapshots private until article deletion; they are publication artifacts, not a replacement for the separate edit-history story M23.
6. Change public article/list/search/collection queries to use active snapshots. Update collection published counts and deletion guards: a collection used by an active snapshot remains nonempty even if the draft moved elsewhere.
7. Change conversation article suggestions to rank only active snapshot content. Adapt snapshots into the ranker's input without attaching synthetic Article entities to EF. If M24 exists, related public articles must use the same source.
8. Keep draft-name/slug validation and active-route uniqueness distinct. A conflicting publish returns 409 and retains the previous live route. Deleting an article removes active route and snapshots transactionally; use explicit ordering when foreign-key dependencies require more than one SaveChanges inside that transaction.
9. Test draft edits to live articles, old/new URL behavior, public collection counts, unpublished drafts, slug contention, simultaneous publication, deletion, rollback, and migrated old Published articles.
10. Run all help-center/ranking/authorization/migration tests. If S10 exists, route scheduled publication through this service and preserve its stale-content checks.

**Concrete fixture and failure rehearsal.** A is live at `/refunds` in Collection X. Draft moves to `/refunds-new` in Y: public `/refunds` and X's published count stay unchanged. Publishing atomically moves the live route/content to the new snapshot. Fail that commit: readers still get the complete old version, not a mixed title/body or a missing route.

**Run:** `dotnet test --filter "FullyQualifiedName~S17|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~ArticleSuggestionTests"` plus migration checks.

**Rollout and limits.** Backfill and verify public equivalence before switching readers, then switch writers together. Old readers would expose unapproved editorial content after new draft edits, so this is not a safe mixed-version rollout or blind code rollback. Preserve the snapshot-aware public path until a deliberate data reconciliation is complete.

**Teach it back / done when.** Explain why “Status=Published” no longer tells you which text is public. Complete when every public discovery/read path uses one approved snapshot and draft edits cannot leak into it.

<a id="s18"></a>
## S18: Atomically claim the next unassigned conversation

**User story.** As an available teammate ready for more work, I want one command to assign me the next eligible unassigned conversation so I do not compete by manually opening and assigning the same thread as another teammate.

**Learn here.** Reading a candidate and assigning it in independent transactions leaves a race. Capacity checking, candidate selection, assignment, audit, and queued events form one command.

**Open first:** [ConversationsController.Assign](../src/Harbor.Api/Controllers/ConversationsController.cs), [AutoAssigner](../src/Harbor.Infrastructure/AutoAssigner.cs), [Conversation](../src/Harbor.Domain/Entities/Conversation.cs), [AssignmentRulesTests](../tests/Harbor.Tests/Integration/AssignmentRulesTests.cs), and [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs). Create `S18ClaimNextTests.cs` with separate connections.

**Contract and acceptance criteria.** Both roles may POST `api/workspaces/{workspaceId}/conversations/claim-next`, optionally restricting InboxId, with identity always taken from claims. Caller must be Available and below their current non-Closed assigned-work capacity; otherwise 409. Invalid related inbox gives 422. Select Open work with neither teammate nor team assignment, ordered priority descending, CreatedAt ascending, then ID. Return 200 summary for one claimed thread or 204 if none exists. Preserve state, messages, targets, and LastMessageAt. Stage the normal manual-assignment history and ConversationAssigned webhook. No bulk claims, automatic queue rebalancing, or change to existing manual assignment policy.

**Implementation plan.**

1. Arrange Urgent/High/Normal unassigned Open threads plus older Closed, Snoozed, and team-assigned threads. Write the exact expected candidate order.
2. Add a small request contract and scope the optional inbox. Do not accept a target teammate ID; the feature claims work for the caller only.
3. Begin an immediate write transaction before reading caller availability, non-Closed workload, or candidates. Read all three through the same context/transaction so the capacity check is not based on an earlier snapshot.
4. Reject an unavailable/full caller; null capacity means unlimited. Count Open plus Snoozed assignments, matching current eligibility behavior. Do not call `PickNextAsync`, which selects someone else and moves the inbox pointer.
5. Query one eligible conversation with deterministic ordering. Return 204 without an assignment event when empty. Use the existing conversation token to protect the selected entity.
6. Extract/reuse manual assignment staging: call AssignToTeammate, record prior/current IDs with ActorTeammateId from claims, and stage ConversationAssigned. Save and commit before returning the summary.
7. Handle known busy/lock failures with the shared bounded transaction retry rule and fresh reads. Never repeat an already committed claim because the HTTP response was lost; this new endpoint has no client idempotency receipt.
8. Preserve ordinary manual assignment semantics, including any ability to exceed capacity deliberately. This feature prevents its own command from claiming when full; it does not create a universal capacity invariant across all existing writes.
9. Test two teammates racing for one candidate, two calls for one teammate with one remaining capacity slot, priority/time ties, no work, optional inbox, foreign workspace, and rollback of assignment plus outbox on failure.
10. Run assignment/concurrency/outbox regressions. No schema is required; inspect the query plan before proposing an index, and add a migration only if an actual index change is justified.

**Concrete fixture and failure rehearsal.** Two candidates and claimant A with one free slot: two competing calls commit at most one new assignment to A; the other sees full capacity or a documented transient retry outcome. Two different teammates competing for one thread cannot both receive that same committed claim. A failed save leaves it unassigned and creates no audit or delivery row.

**Run:** `dotnet test --filter "FullyQualifiedName~S18|FullyQualifiedName~AssignmentRulesTests|FullyQualifiedName~ConcurrencyTests|FullyQualifiedName~WebhookOutboxTests"`.

**Rollout and limits.** Add the command without changing existing assignment endpoints. Monitor lock contention and cap backend retries. Disabling it leaves previously assigned work intact. If clients need retry-after-response-loss safety, specify a separate receipt contract before adding that behavior; S01 only covers creation.

**Teach it back / done when.** Explain why SELECT followed by UPDATE is insufficient without the transaction and predicates that connect them. Complete when contested claims have one correct committed owner and capacity decisions use the same transaction as assignment.

<a id="s19"></a>
## S19: Block conversation closure until required checklist items are complete

**User story.** As a teammate handling a multi-step issue, I want required checklist items to block closure until they are complete so an important verification step cannot be skipped accidentally.

**Learn here.** The invariant crosses a parent conversation and child checklist rows. Protecting only the item or only the close endpoint is insufficient when someone adds a required item while another person closes the thread.

**Open first:** [Conversation.Close](../src/Harbor.Domain/Entities/Conversation.cs), [ConversationsController.ChangeState](../src/Harbor.Api/Controllers/ConversationsController.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and [ConversationStateMachineTests](../tests/Harbor.Tests/Integration/ConversationStateMachineTests.cs). Create `S19ClosureChecklistTests.cs`.

**Contract and acceptance criteria.** Both roles may create/list checklist items at `api/conversations/{id}/checklist-items`, maximum 50 per thread. Items have a trimmed 1–200 character title, required boolean, completion time/actor, and version. Scoped PUT/DELETE at `api/checklist-items/{id}` and PUT `/completion` require current ExpectedVersion; stale gives 409. Item mutation on a Closed conversation gives 409. Closing with any incomplete required item gives 422 and those item IDs; optional items never block. Completing an item does not close the conversation automatically. No templates, external tasks, reminders, or Admin override in this slice.

**Implementation plan.**

1. Write a state table: no items → close allowed; incomplete optional → allowed; incomplete required → blocked; required completed → allowed. Include Snoozed as an allowed state before closing.
2. Add `ConversationChecklistItem` with conversation/workspace IDs, title, Required, completion fields, created time, and concurrency token. Configure cascade with conversation and index for incomplete required lookup; generate `S19ConversationChecklists`.
3. Add contracts validating required boolean presence, title, item count, and expected version. Derive completion actor from claims; preserve first completion time on repeated already-completed commands with a valid version.
4. Implement scoped item CRUD/completion. Every actual item mutation must also mark the parent conversation modified and touch UpdatedAt so its Version changes. Force a parent update even when a fake-clock timestamp equals its previous timestamp; never change LastMessageAt.
5. Before mutating, check the parent's current non-Closed state. Enforce the 50-item limit in the same short transaction as insertion and parent update.
6. Extract a close workflow that loads required-item state and invokes existing Close only when no blocker remains. Keep breach detection, first-resolution stamping, and ConversationClosed publication in their existing atomic commit.
7. Require the close and item mutations to participate in the parent's version check. In a race, either the close sees the new required item and rejects it, or one stale writer fails with 409; do not let both incompatible changes commit.
8. Keep existing Open/Snooze/contact-reply behavior unchanged. Reopening does not clear checklist completion or reset FirstResolvedAt. Optional incomplete items can remain visible on a Closed thread but cannot be edited until it is reopened.
9. Test close-versus-add, close-versus-completion reversal, stale item editing, item limits, both roles, foreign items, and zero outbox/breach effects from a rejected close.
10. Run state/messaging/SLA/concurrency regressions and migration checks. Document the blocker response so a future client can show the exact outstanding items.

**Concrete fixture and failure rehearsal.** Required R is incomplete and optional O is incomplete: close gives 422 naming only R. Complete R and close succeeds even though O remains. Race a close against adding a new required R2: final state must not be Closed with R2 newly committed incomplete. On conflict, reload and deliberately retry the user's intended operation.

**Run:** `dotnet test --filter "FullyQualifiedName~S19|FullyQualifiedName~ConversationStateMachineTests|FullyQualifiedName~SlaEngineTests|FullyQualifiedName~ConcurrencyTests"` plus migration checks.

**Rollout and limits.** Deploy item storage/readers and the close guard together before allowing required items. Old close writers bypass the guard, so prevent mixed-version mutation traffic. Disabling new checklist creation must not disable enforcement for existing required items.

**Teach it back / done when.** Explain why an item edit also changes its parent's version. Complete when no supported concurrent write sequence can close a conversation while committing a new incomplete required item against that same state.

<a id="s20"></a>
## S20: Archive an inbox without losing its existing conversations

**User story.** As a workspace Admin retiring a support queue, I want to archive its inbox so new conversations cannot start there while staff can still find and finish its existing threads.

**Learn here.** Archival is a resource lifecycle rule, not deletion. It must be enforced at every creation entry point, including inbound email, while existing-thread behavior and historical reports keep working.

**Open first:** [InboxesController](../src/Harbor.Api/Controllers/InboxesController.cs), [Inbox](../src/Harbor.Domain/Entities/Inbox.cs), [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs), [EmailController](../src/Harbor.Api/Controllers/EmailController.cs), and [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs). Create `S20InboxArchiveTests.cs`.

**Contract and acceptance criteria.** Admin PUT `api/inboxes/{id}/archive` accepts required Archived=true/false and returns 200 with inbox metadata including nullable ArchivedAt. Repeating the same setting is a no-op. Inbox listing defaults to active only and accepts IncludeArchived=true; scoped GET-by-ID still returns archived inboxes. Starting a chat or a new email thread in an archived inbox gives 422. A correctly matched existing email thread in that same inbox may receive replies; validate its sender against the thread's contact. Preserve all conversations, messages, assignment, targets, inbox email address, and AutoAssign configuration. Archived addresses remain reserved by the existing unique index. Restoring permits new starts again.

**Implementation plan.**

1. Write the behavior matrix for active/archived × chat start/new email/existing email reply/existing manual reply/list/detail. Make the creation-versus-continuation distinction explicit.
2. Add nullable ArchivedAt to Inbox and the safe response mapping; generate `S20InboxArchival` with existing inboxes active. Do not add IHasVersion to Inbox merely for this field; that would also alter the deliberate round-robin pointer policy.
3. Implement the Admin archive/restore action using a short write transaction. Preserve the original ArchivedAt on repeated archive and clear it on restore; touch no conversation rows.
4. Add a shared active-inbox creation guard used by ConversationStarter's callers. For creation, acquire an immediate write transaction before reading current inbox status and keep validation plus creation commit together so archive cannot slip between them.
5. If another feature already owns the transaction, enlist in it rather than beginning a nested transaction. Apply this to normal chat, new inbound email, and routing's intermediate contact save if S08 exists.
6. Refactor inbound ordering: resolve inbox and any replay receipt, locate/validate an existing thread, then decide whether this is continuation or a new start. Reject an archived new start before creating a sender contact. An S02 receipt replay returns its original committed result even after archival.
7. Require an existing-thread continuation to match the selected inbox and normalized sender/contact email; reject a conflicting reference with 422. Continue ordinary existing chat/email replies and allow staff to claim or close old work.
8. Add active/default listing and IncludeArchived binding without removing archived history from conversation queries or reports. If a transfer feature from M07 exists, also reject transfers into an archived destination through the shared lifecycle guard.
9. Test archive/restore repeats, current address reservation, existing replies, no orphan contacts on rejection, new-start-versus-archive races, Agent 403, foreign inbox, and unchanged historical report totals.
10. Run inbox/email/channel/report/concurrency regressions and migration checks. Verify repeated demo seeding does not reactivate an intentionally archived inbox.

**Concrete fixture and failure rehearsal.** Inbox I contains C1. Archive I: starting C2 fails, a validated email reply to C1 succeeds, and C1 remains in reports. If archive commits before a racing creation reads status, creation rejects; if creation holds the write transaction first, its conversation commits before archive. No committed creation may be based on a stale pre-archive check afterward.

**Run:** `dotnet test --filter "FullyQualifiedName~S20|FullyQualifiedName~WorkspaceEndpointsTests|FullyQualifiedName~EmailChannelTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~ReportingTests"` plus migration checks.

**Rollout and limits.** Deploy all ingress guards before enabling archive writes. Old creators ignore ArchivedAt, so prevent mixed-version creation traffic after archival begins. Code rollback must retain the guard or explicitly restore inboxes through a reviewed product action; silently accepting new work would violate the lifecycle contract.

**Teach it back / done when.** Explain why default-list filtering alone cannot archive an inbox and why a reply is different from a new conversation. Complete when every creation path honors archival while existing support history remains usable.

## Review your work as its future maintainer

After implementing one story, bring these artifacts to review:

- A one-sentence user benefit and invariant, with a small before/after example.
- A request/worker sequence drawing showing transaction commits, external effects, and each crash boundary.
- A list of changed entry points and the existing regression tests that cover them.
- A migration upgrade result using old data, plus the deployment order needed to keep mixed versions safe or avoid running them together.
- The specified failure rehearsal: expected rows/files/sends, observed result, and behavior on retry or restart.
- Explicit limits: data size, batch size, retention, retry budget, ownership, and guarantees you intentionally did not claim.
- A short handover explaining how to tell success, a recoverable conflict, an exhausted retry, and a data inconsistency apart.

Repeat the explanation three times on different days: first as the user's workflow, next as the data model, and finally as a failure timeline. Use [repeat and recall](13-repeat-and-recall.md), [pairing dialogues](12-pairing-dialogues.md), and [the review rubric](../docs/learning/review-rubric.md) to make that practice concrete.

If you are stuck, report: “In S__, step __, I expected __ after the commit boundary __. Instead I observed __ in the database/file/fake sender. The last successful assertion was __. My hypothesis is __.” That turns a large feature into one testable question without pretending the difficult part is already solved.
