# 30 midlevel feature stories: learn Harbor by extending it

**Original planning briefs, now accompanied by implementation.** The source contains the worked features; see the [implementation bootcamp](bootcamp/README.md) and [verification tracker](bootcamp/progress.md). These plans preserve their original baseline observations and proposed file names for learning. The current [API reference](../docs/api-reference.md) describes the integrated behavior, including later senior-story changes. The [25 junior stories](16-junior-user-stories.md) remain a separate learning sequence.

These stories help you practice moving from “I can edit a method” to “I can own a small feature across contracts, business rules, persistence, and tests.” They deliberately keep the product decisions small and explicit. Easy-to-follow steps do not mean every step is easy: SQL translation, migration failures, and concurrency may require a separate learning session.

## Choose a story

| Story | New capability | Main learning | Schema change | Dependency |
| --- | --- | --- | --- | --- |
| [M01](#m01) | Personal saved conversation queues | Stored configuration and query reuse | Yes | None |
| [M02](#m02) | Personal conversation bookmarks | Ownership and relational queries | Yes | None |
| [M03](#m03) | Private reply drafts with conflict detection | Optimistic concurrency | Yes | None |
| [M04](#m04) | Safe conversation subject editing | Client preconditions and domain methods | No | None |
| [M05](#m05) | Atomic bulk priority changes | Transactions and shared write behavior | No | None |
| [M06](#m06) | Replace a conversation's complete tag set | Set reconciliation and atomic writes | No | None |
| [M07](#m07) | Transfer a conversation to another inbox | Invariants across SLA and routing | Yes, transfer history | None |
| [M08](#m08) | Wake a bounded batch of due snoozes | Deterministic time and repeatable commands | No | None |
| [M09](#m09) | Unified conversation activity timeline | Merging paged event sources | No | None |
| [M10](#m10) | Explain assignment eligibility | Extracting reusable decision logic | No | None |
| [M11](#m11) | Patch contact attributes safely | Partial updates and JSON validation | Yes, version token | None |
| [M12](#m12) | Find likely duplicate contacts | SQL grouping and normalization | No | None |
| [M13](#m13) | Contact support-history summary | Independent aggregates without double counting | No | None |
| [M14](#m14) | Preview unsaved segment rules | Expression queries and validation | No | None |
| [M15](#m15) | Clone a segment under a new name | Copy semantics and uniqueness | No | None |
| [M16](#m16) | Explain why a contact matches a segment | SQL truth semantics and diagnostics | No | None |
| [M17](#m17) | Simulate an SLA policy decision | Pure decision logic and provenance | No | None |
| [M18](#m18) | List upcoming unresolved SLA targets | Deadline projections and ordering | No | None |
| [M19](#m19) | Report backlog age | Time buckets and report cohorts | No | None |
| [M20](#m20) | Report team queues and roster size | Attribution and independent aggregates | No | None |
| [M21](#m21) | Download conversation volume as CSV | Shared report logic and file contracts | No | None |
| [M22](#m22) | Clone an article into a new draft | Publishing boundaries and identity | No | None |
| [M23](#m23) | Keep article revision history | Atomic snapshots and migration compatibility | Yes | None |
| [M24](#m24) | Show related public articles | Ranking and publication isolation | No | None |
| [M25](#m25) | Collect teammate feedback on articles | One vote per user and aggregates | Yes | None |
| [M26](#m26) | Preview canned replies with named placeholders | Bounded parsing and pure rendering | No | None |
| [M27](#m27) | Send a rendered canned reply | Reusing the real message workflow | No | M26 |
| [M28](#m28) | Dispatch webhooks in bounded batches | Backpressure and retry eligibility | No | None |
| [M29](#m29) | Redrive a failed webhook while keeping history | Idempotency and database uniqueness | Yes | None |
| [M30](#m30) | View webhook delivery health | Operational definitions and aggregate queries | No | None |

Suggested first picks: M14 for queries, M22 for writes, or M17 for business rules. Try M03, M23, and M29 after completing a smaller story. M27 requires M26; all other stories can start from the present codebase. If another story has already added a helper or contract, extend that implementation instead of creating a second version.

## The codebase in three repeated explanations

**As a request journey:** an HTTP request enters a controller, binds to a request contract, passes authentication and workspace checks, invokes business behavior or a database query, saves if necessary, and maps to a response contract. Follow [a read](04-follow-a-read.md) and [a write](05-follow-a-write.md) before changing one.

**As responsibilities:** `Harbor.Api` translates HTTP into work; `Harbor.Domain` names the concepts and rules; `Harbor.Infrastructure` connects those concepts to SQLite, assignment, segments, and webhooks. A controller should not invent a second version of an existing rule just because its endpoint is new.

**As questions to ask while reading:** Who is allowed to do this? Which rows belong to that person or workspace? What must remain true after the change? When does the database commit? What observation would prove the feature works? These are the same request journey expressed as a review checklist.

## A repeatable implementation routine

1. Choose **one** story. Copy its acceptance criteria into your issue or learning journal. Explain its user benefit aloud in one sentence.
2. Open the linked controller, supporting code, and example test. Trace the closest existing endpoint before editing. Use `rg "MethodName" src tests` to find callers.
3. Draw the proposed flow: request → scope check → validation → query/rule → save → response. For reads, remove the save step. Write down every new field's null meaning.
4. Add the proposed test class under `tests/Harbor.Tests/Integration`, using that story's `MXX` prefix. Write one HTTP test and run its filter. Confirm a test was actually discovered and fails because the feature is missing; a zero-test run proves nothing.
5. Implement the smallest successful request. Stop and explain why each file changed. Then implement the specified invalid, unauthorized, empty, and conflicting cases.
6. Add a domain unit test when a pure decision or state transition has meaningful boundary cases. Use the real SQLite integration harness for queries, foreign keys, transactions, and uniqueness.
7. Run the story's focused tests and named existing regressions. If the schema changes, run the migration checks below. Finish with `dotnet test --configuration Release` and `python scripts/check-doc-links.py` after documenting the new API.
8. Review your diff. Record the exact commands and outcomes, one tradeoff, and one limitation. Do not claim a migration, test, endpoint, or deployment exists merely because this plan mentions it.

Use 45–90 minute sessions: understand and specify; build the successful path; cover failures; review and explain. Larger stories may need several sessions per stage. At each break write your next concrete action, such as “add the foreign-workspace assertion,” instead of “finish feature.”

### Common implementation details you will reuse

- Existing contracts live in [Requests.cs](../src/Harbor.Api/Contracts/Requests.cs) and [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs). Put new request/response records there initially. New entity classes belong under `src/Harbor.Domain/Entities`; map them in [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs).
- [ClaimsPrincipalExtensions](../src/Harbor.Api/Infrastructure/ClaimsPrincipalExtensions.cs) provides `GetWorkspaceId()` and `GetTeammateId()`. Derive ownership from these claims; a request must not choose another owner. Personal resources stay private even from a different Admin in the same workspace.
- Workspace routes for another tenant normally produce **403** through [WorkspaceScopeFilter](../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs). A resource-by-ID query includes the caller's workspace and returns **404** for missing or foreign rows. Missing authentication gives **401**; a forbidden role gives **403**. Related IDs in a valid owner's command give **422** when unavailable unless the story specifies otherwise. Do not expose which foreign tenant owns an ID.
- Request shape errors use **400**; validly shaped but unusable business input uses **422**; conflicts use **409**. Provide `ProblemDetails`. Validate nullable enum fields explicitly; deserialization can accept undefined numeric enum values. For required JSON booleans, a required nullable boolean distinguishes omitted input from `false`.
- [Paging](../src/Harbor.Api/Infrastructure/Paging.cs) uses array bodies plus `X-Total-Count`, `X-Page`, `X-PageSize`, and `X-TotalPages`. Default size is 50 and maximum is 200. Filter and order before `ToPageAsync`; do not load all rows just to page them. Add a unique final ordering key. Paging across changing data is not a historical snapshot.
- [ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs) creates typed fixtures. Creating a workspace also creates a bootstrap Admin **and switches the client's active key**. Create fresh workspaces per test; switch back with `ActAsAdminOf` after constructing a foreign workspace. Do not assume the teammate table starts empty.
- [HarborApiFactory](../tests/Harbor.Tests/Integration/HarborApiFactory.cs) exposes `WithDb` for arranging deterministic rows. Set exact UTC instants instead of sleeping. A new conversation already contains its opening contact reply. Notes change activity but do not count as a teammate first response.
- [Conversation](../src/Harbor.Domain/Entities/Conversation.cs) already has a `Version` concurrency token; the context refreshes tokens on modified `IHasVersion` entities. That protects overlapping database writes. Detecting a stale client requires also comparing a client-supplied expected version. Merely returning a token does not enforce it.
- One `SaveChangesAsync` makes its tracked database changes atomic. It does not make earlier HTTP calls atomic. [Webhooks.PublishAsync](../src/Harbor.Infrastructure/Webhooks.cs) stages outbox rows; [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs) sends them later. The current dispatcher sends before saving results: duplicate delivery after a crash or overlapping dispatch remains possible. None of these stories promises exactly-once HTTP delivery.
- No story needs a new frontend, external service, production deployment, or a background scheduler. New scheduled-looking actions here are explicit HTTP commands. Write cancellation-aware database calls when adding asynchronous paths.

### Schema-change recipe

Read [the testing guide's migration caveat](../docs/testing.md): integration tests use `EnsureCreated`, while application development startup uses migrations. Both paths must work.

For each story marked “Yes” in the table:

1. Add the entity/property and its EF configuration, including ownership indexes, maximum lengths, and explicit foreign-key deletion behavior. Mirror existing workspace scoping; do not assume a foreign key proves two rows have the same workspace.
2. From the repository root run `dotnet ef migrations add MXXFeatureName --project src/Harbor.Infrastructure --startup-project src/Harbor.Api`, replacing the name with that story's suggested migration name. If `dotnet` is unavailable, follow [run and observe](10-run-and-observe.md); do not change target frameworks to work around local setup.
3. Inspect the generated migration and snapshot. Existing data must receive the documented default or backfill. Do not reset or delete an existing database to hide a migration problem.
4. Run `dotnet test --filter "FullyQualifiedName~MigrationDrift"` and test a disposable database upgraded from the previous migration with representative old rows. Verify those rows survive and the feature works after upgrade.
5. Document rollback implications. Dropping a new drafts/history table loses those new records; use a disposable database for downgrade experiments. A schema rollback is not a production recovery plan.

New test files named below are **to create during implementation**, not files supplied by this document. Commands are future validation instructions, not reports of tests already run.

<a id="m01"></a>
## M01: Save a personal conversation queue

**User story.** As a teammate, I want to save a named set of queue filters so I can reopen the same working queue without rebuilding its URL.

**Learn the codebase.** The existing list already shares its filtering with the overview through `ConversationQueries`. A saved queue should store a recipe for a query, not a frozen list of conversation IDs. Reopening it evaluates today's data.

**Open first:** [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs), [SegmentQueries](../src/Harbor.Infrastructure/SegmentQueries.cs), and [ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs). Create `M01SavedQueueTests.cs`.

**Contract and acceptance criteria.** Add personal CRUD at `api/workspaces/{workspaceId}/saved-queues` and `api/saved-queues/{id}`, plus `GET api/saved-queues/{id}/conversations?page=...&pageSize=...`. Both roles may manage only their own rows. Create returns 201, update/get 200, delete 204; another owner's ID gives 404. Store a trimmed 1–100 character name and a versioned JSON filter document. Duplicate names are allowed. Persist filter criteria and sort, excluding page/pageSize. An unavailable saved segment yields 422 when executing the queue; do not silently broaden it. No shared queues or overview endpoint in this slice.

**Implementation plan.**

1. Create fixtures with one Open and one Closed conversation and two teammates. First test: saving `State=Open` and executing returns only the Open ID.
2. Define a dedicated saved-filter DTO containing the existing non-paging filter fields. Add explicit enum/date validation and a mapping to `ConversationFilterRequest`; never deserialize arbitrary CLR type names.
3. Add `SavedQueue` with ID, workspace ID, owner teammate ID, name, filter JSON, schema version 1, and created/updated timestamps. Index `(WorkspaceId, OwnerTeammateId)`; configure owner/workspace foreign keys and cascade deletion. Generate `M01SavedQueues`.
4. Implement scoped create/get/list/update/delete. List only the caller's rows, ordered by name then ID, with ordinary paging. Enforce a serialized filter limit of 10,000 characters before saving.
5. On execution, load by ID + workspace + owner; deserialize the supported schema, validate it, and map the caller's paging into the runtime filter. Invalid/unsupported stored configuration gives a descriptive 422.
6. Reuse `BuildAsync`, `Order`, tag inclusion, and summary mapping from the existing list. Extract a small query execution helper only if it removes real duplication; do not call one controller from another.
7. Add isolation, paging, unknown segment, round-trip, and dynamic membership tests. Changing a conversation to Closed must remove it on the next execution.
8. Run focused and queue regressions, migration checks, and document the JSON version rule and routes.

**Concrete checks.** Owner A sees one queue; owner B sees none. A saved page 99 must not exist in the stored DTO. `pageSize=1` still counts every matching conversation. Deleting its segment produces 422 and zero fallback results.

**Run:** `dotnet test --filter "FullyQualifiedName~M01|FullyQualifiedName~ConversationQueueTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why a saved filter is data while `IQueryable` is executable query composition. Serializing an EF query or caching its IDs solves a different problem.

**Done when:** saved configuration survives a database reload, executes current filters correctly, and cannot be read through another teammate's credentials.

<a id="m02"></a>
## M02: Bookmark conversations for yourself

**User story.** As a teammate, I want a private bookmarked queue so I can return to conversations I am investigating without changing their assignment.

**Learn the codebase.** Assignment is shared conversation state; a bookmark belongs to a person. Model the relationship separately so adding a bookmark does not touch SLA, activity, or routing.

**Open first:** [TagsController](../src/Harbor.Api/Controllers/TagsController.cs) for a join-table write, [ConversationTag](../src/Harbor.Domain/Entities/ConversationTag.cs), [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), and [AuthorizationTests](../tests/Harbor.Tests/Integration/AuthorizationTests.cs). Create `M02BookmarkTests.cs`.

**Contract and acceptance criteria.** `PUT/DELETE api/conversations/{id}/bookmark` sets/clears the caller's bookmark and returns 204, including repeat requests. A missing/foreign conversation gives 404 for either action. `GET api/workspaces/{workspaceId}/bookmarked-conversations` returns paged conversation summaries newest bookmark first, then conversation ID. Both roles are allowed, but nobody chooses an owner in the body. List totals count bookmarks belonging to the caller, not all tenant bookmarks. No notifications, shared bookmark folders, or new conversation filters.

**Implementation plan.**

1. Write a test where two teammates bookmark the same conversation and only one removes their bookmark; the other's list must retain it.
2. Add `ConversationBookmark` with workspace ID, teammate ID, conversation ID, and created timestamp. Use a unique/composite key `(TeammateId, ConversationId)` and ownership/list indexes. Configure cascade deletion from the parent relationships; add `M02ConversationBookmarks`.
3. Make each mutation check the conversation with ID + caller workspace before inspecting bookmarks. Use the claims teammate ID in every bookmark predicate.
4. For PUT, check for an existing row and insert only if absent. For a concurrent duplicate insert, catch only the relevant uniqueness violation, reload in a clean tracking state, and return 204 if that same bookmark now exists. Do not turn all database errors into success.
5. For DELETE, remove only the caller's row if present. The parent still must exist even when there is nothing to remove.
6. Build the list as a scoped bookmark-to-conversation query. Order before paging, load tags only for the resulting page, and use existing summary mappings. Inspect that total count and selected rows share the owner predicate.
7. Add repeat PUT/DELETE, equal-time order, paging, missing parent, same-workspace different-owner, and foreign-workspace tests. Snapshot conversation timestamps and assignment before the mutations and compare afterward.
8. Run authorization and pagination regressions, migration checks, then explain the new ownership relationship in the API documentation.

**Concrete checks.** A bookmarks C1 and C2; B bookmarks C2. A's total is 2, B's total is 1. After A removes C2, both totals are 1. Neither bookmark action changes `UpdatedAt`, `LastMessageAt`, or an assignee.

**Run:** `dotnet test --filter "FullyQualifiedName~M02|FullyQualifiedName~PaginationBoundaryTests"` plus the schema recipe.

**Explain it back / common trap.** Point to the database uniqueness guarantee and the query ownership guarantee. An application-level existence check alone does not prevent simultaneous duplicate inserts.

**Done when:** private bookmarks remain independent, retries are harmless, and list paging is stable for an unchanged dataset.

<a id="m03"></a>
## M03: Save a private reply draft without overwriting a newer draft

**User story.** As a teammate composing a reply, I want to save my private draft and detect a stale browser tab so my newer wording is not silently lost.

**Learn the codebase.** A draft is not a `Message`: saving it must not update first-response time, trigger email rendering, or publish a webhook. This story introduces client-visible concurrency on a separate resource.

**Open first:** [IHasVersion](../src/Harbor.Domain/IHasVersion.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), [ConcurrencyExceptionHandler](../src/Harbor.Api/Infrastructure/ConcurrencyExceptionHandler.cs), and [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs). Create `M03ReplyDraftTests.cs`.

**Contract and acceptance criteria.** Add GET/PUT/DELETE at `api/conversations/{id}/my-draft`. Scope by caller workspace and teammate. GET returns 200 with body, version, updated time, or 404 when absent. PUT accepts a required nonblank body up to 20,000 characters plus nullable `ExpectedVersion`: null means create only; an existing draft requires its exact version. Create returns 201, update 200, stale/conflicting creation 409. DELETE requires the current version in a query parameter and returns 204; missing draft gives 404. No automatic send or autosave frontend.

**Implementation plan.**

1. Write the two-tab scenario: both read V1; tab A saves V2; tab B submits V1 and receives 409; GET retains A's text.
2. Add `ReplyDraft : IHasVersion` with workspace, conversation, owner IDs, body, version, and updated timestamp. Unique `(ConversationId, OwnerTeammateId)`; configure deletion with parents. Generate `M03ReplyDrafts`.
3. Implement a reusable scoped parent/draft lookup. Do not accept an author ID, a channel, or message kind from the draft request.
4. Compare `ExpectedVersion` against the loaded token before editing. Keep the original tracked token for EF's UPDATE predicate so a second writer arriving after the check also conflicts.
5. Handle simultaneous create uniqueness narrowly as 409. Require a nonempty GUID for update/delete preconditions; malformed or missing required delete input gives 400. A no-change PUT with a valid token may return the same version.
6. Save only the draft row. Return the post-save token. Apply the same expected-version check to DELETE, including EF's concurrent-delete failure.
7. Add create/get/delete, two owners, both roles, empty/oversized body, stale update/delete, and missing/foreign parent tests. Record message count, delivery count, and conversation timing before a save and assert equality afterward.
8. Test migration upgrade and focused concurrency cases, then document how a client recovers from 409: reload, compare text, and deliberately retry.

**Concrete checks.** A's draft says “Checking now”; B's says “Need logs” for the same thread. Neither sees the other's draft. A valid update changes only A's row. A draft on an email conversation creates no email message ID or outbox delivery.

**Run:** `dotnet test --filter "FullyQualifiedName~M03|FullyQualifiedName~ConcurrencyTests"` plus the schema recipe.

**Explain it back / common trap.** Describe the gap between checking a token in C# and atomically checking it in SQL. You need both client intent and database protection.

**Done when:** sequential stale tabs and overlapping writes cannot silently replace a newer draft, and draft saves have no message side effects.

<a id="m04"></a>
## M04: Edit a conversation's subject with a version precondition

**User story.** As a teammate, I want to correct a conversation's subject while receiving a conflict if the conversation changed since I opened the editor.

**Learn the codebase.** Conversation responses currently omit the entity's existing `Version`. A dedicated editor resource can expose it without changing every existing response contract.

**Open first:** [Conversation](../src/Harbor.Domain/Entities/Conversation.cs), [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), [Response mappings](../src/Harbor.Api/Contracts/Responses.cs), and [ConversationTests](../tests/Harbor.Tests/Unit/ConversationTests.cs). Create `M04SubjectEditingTests.cs`.

**Contract and acceptance criteria.** GET `api/conversations/{id}/subject` returns subject + version. PUT at the same route requires a nonempty `ExpectedVersion` and accepts nullable subject up to 500 characters. Trim outside whitespace; blank/null clears the subject. Return 200 with the saved subject and version. Stale version gives 409, invalid shape 400, missing/foreign conversation 404. Both roles may edit. Actual changes update `UpdatedAt`, but not `LastMessageAt`, state, assignment, SLA targets, or messages. No new schema or webhook event.

**Implementation plan.**

1. Write the successful GET/PUT round trip and a stale-client test. Use a new response record containing only ID, subject, and version.
2. Add a `ChangeSubject` domain method accepting subject and `now`. Normalize outer whitespace and return without touching timestamps when normalized text equals the stored value. Put the domain invariant for the normalized maximum length there as well.
3. Add request-level maximum length and expected-version validation. Decide explicitly that raw input over 500 characters is 400 even if trimming would shorten it.
4. Load the conversation with ID + workspace. Compare the request token before calling the domain method. Let the existing EF token and exception handler handle a race after that comparison.
5. Save once and return the token after saving. A valid no-op returns the original token. Do not manually set `Version`; the context owns version changes.
6. Write domain tests for clear, trim, internal spaces, no-op, and unrelated-field preservation. Use different fixed times so `UpdatedAt` changes are observable.
7. Add HTTP tests for stale token after a message or priority change, invalid GUID, missing token, oversized subject, both roles, and foreign access. A new message can make this precondition stale even when the subject itself did not change; document that whole-conversation rule.
8. Run the state/search/concurrency regressions. Confirm subject searches immediately reflect the new value and document the editor endpoint.

**Concrete checks.** “  Refund question  ” becomes “Refund question”; “   ” becomes null. Read V1, change priority through its existing endpoint, then PUT subject with V1: 409 and no subject change. A successful edit keeps the message count constant.

**Run:** `dotnet test --filter "FullyQualifiedName~M04|FullyQualifiedName~ConversationSearchTests|FullyQualifiedName~ConcurrencyTests"`.

**Explain it back / common trap.** Explain why `UpdatedAt` and `LastMessageAt` differ. Changing the subject should not pretend that someone sent a message.

**Done when:** callers can correct or clear a subject, conflicts are visible, and existing lifecycle behavior stays consistent.

<a id="m05"></a>
## M05: Change priority for a small batch atomically

**User story.** As a teammate triaging an incident, I want to set one priority on up to 25 conversations in one request so the whole selected batch changes together.

**Learn the codebase.** `SetPriority` does more than set an enum: it recalculates targets from creation time and detects breaches. Bulk work must preserve that complete behavior.

**Open first:** [ConversationsController.SetPriority](../src/Harbor.Api/Controllers/ConversationsController.cs), [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), [SlaBreaches](../src/Harbor.Infrastructure/SlaBreaches.cs), and [SlaEngineTests](../tests/Harbor.Tests/Integration/SlaEngineTests.cs). Create `M05BulkPriorityTests.cs`.

**Contract and acceptance criteria.** POST `api/workspaces/{workspaceId}/conversations/bulk-priority` accepts 1–25 distinct conversation IDs and a required validated priority. Duplicate/empty/oversized lists or undefined priorities give 400. Any missing/foreign ID gives a generic 422 and **zero changes**. Both roles may use it. Return 200 with summaries ordered by ID. A concurrent write conflict gives 409 and rolls back the batch. No partial-success response, automatic retry, or new priority webhook.

**Implementation plan.**

1. Arrange two inboxes with different SLA policies and one conversation in each. Test one request updates both priorities and applies each inbox's own policy.
2. Extract the existing single-conversation priority workflow into a small helper that mutates/stages work but does not save. Keep single-item response and validation behavior compatible.
3. Add the bulk request and validate shape before querying. Query all selected conversations with workspace scope and required tags/inboxes; compare distinct requested count to found count before mutating anything.
4. Capture one `now` for the batch. Invoke the shared workflow on each loaded conversation. Load reusable policy data once if doing so keeps the resolver shared; with a maximum of 25, avoid a broad architecture rewrite solely for optimization.
5. Keep breach rows staged with their conversations. Call `SaveChangesAsync` once; its transaction covers all changes. Do not save inside the loop or invoke the single-item HTTP action repeatedly.
6. Add a failure fixture with a valid first ID and unavailable last ID. Assert every priority, target, and breach-row count is unchanged. Check same-priority repetition does not duplicate existing breach events.
7. Exercise a stale tracked conversation using the existing concurrency-test approach; verify all database rows roll back after the failed save, using a fresh context for assertions.
8. Run single-item SLA regressions and document the 25-item limit, atomic behavior, and retry caveat: a later legitimate priority change can be overwritten by a repeated command with new server reads.

**Concrete checks.** C1 was created at 10:00 with a new 15-minute target; C2 at 10:10 with a new 30-minute target. Their due times become 10:15 and 10:40, never “request time + target.” A foreign C3 in the same selection prevents both changes.

**Run:** `dotnet test --filter "FullyQualifiedName~M05|FullyQualifiedName~SlaEngineTests|FullyQualifiedName~SlaEngineEdgeCaseTests"`.

**Explain it back / common trap.** Describe why an atomic batch and a loop of successful single-item requests have different failure behavior.

**Done when:** all selected priorities and SLA effects commit together, with no divergence from the existing single-item rule.

<a id="m06"></a>
## M06: Replace the complete tag set in one request

**User story.** As a teammate, I want to submit the final set of tags for a conversation so I do not need several add/remove requests that can leave an incomplete result.

**Learn the codebase.** `ConversationTag` is a relationship with a composite key. Updating a collection means calculating which relationships to retain, insert, and delete; it does not mean deleting the actual tag records.

**Open first:** [TagsController](../src/Harbor.Api/Controllers/TagsController.cs), [ConversationTag](../src/Harbor.Domain/Entities/ConversationTag.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and [TagAndCannedReplyEndpointsTests](../tests/Harbor.Tests/Integration/TagAndCannedReplyEndpointsTests.cs). Create `M06ReplaceTagsTests.cs`.

**Contract and acceptance criteria.** PUT `api/conversations/{id}/tags` accepts a required array of 0–20 distinct tag IDs. Empty clears every link; null, duplicates, and over-limit input give 400. Missing/foreign conversation gives 404; any unavailable requested tag gives 422 with no changes. Both roles may use it. Return 200 with the resulting tag names sorted using the existing summary contract. Repeating a completed request produces the same set. This is a replacement command, not a stale-client merge; later successful commands may replace earlier selections. No schema or new event type.

**Implementation plan.**

1. Arrange tags A/B/C and a conversation with A/B. Write a test replacing them with B/C; assert precisely those two join rows and all three tag records remain.
2. Define the bounded request and validate duplicates before converting to a set. A set conversion must not silently make malformed duplicate input acceptable.
3. Begin a database transaction before reading the existing links and requested tags. Use the scoped parent query and verify every requested tag belongs to its workspace before editing tracked rows.
4. Compute `remove = current - requested` and `add = requested - current`. Keep intersecting rows attached; remove/add only the required `ConversationTag` entities.
5. Save once and commit. The existing single-tag actions remain available. Catch only identified write/uniqueness conflicts as 409; roll back and let the caller reload. Do not swallow unexpected database failures.
6. Re-query or correctly populate tag navigation data before mapping the response. Do not return stale names from an old tracked collection after deletions.
7. Test empty replacement, repeat request, retained tag identity, unavailable final ID, malformed arrays, both roles, and isolation. Use a fresh context to inspect join-table results after failure.
8. Run tag and queue regressions. Document transaction scope and explain that this feature does not provide a client version precondition against an intentionally later replacement.

**Concrete checks.** `[A,B] → [B,C]` inserts C's link and removes A's link. `[B,C] → []` removes two links and zero tags. `[A,B] → [C,foreign]` keeps `[A,B]`. A `Tag=B` queue search immediately includes the changed conversation when B is retained.

**Run:** `dotnet test --filter "FullyQualifiedName~M06|FullyQualifiedName~TagAndCannedReplyEndpointsTests|FullyQualifiedName~ConversationQueueTests"`.

**Explain it back / common trap.** Explain the difference between deleting a tag and deleting the conversation-to-tag relationship. They affect different users and different tables.

**Done when:** each valid request produces exactly one final set and an invalid request cannot remove even one existing link.

<a id="m07"></a>
## M07: Transfer a chat conversation to another inbox

**User story.** As a teammate, I want to move a misrouted chat conversation into the right inbox while keeping its history and applying the destination's SLA rules.

**Learn the codebase.** Inbox, channel, assignment, and SLA are different concepts. This first slice supports chat conversations only: transferring an email thread needs a separate product decision about sender identity and threading.

**Open first:** [Conversation](../src/Harbor.Domain/Entities/Conversation.cs), [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), and [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs). Create `M07InboxTransferTests.cs`.

**Contract and acceptance criteria.** POST `api/conversations/{id}/transfer` accepts a destination inbox ID. Both roles may transfer a scoped chat conversation; missing conversation gives 404, unavailable destination or an email conversation gives 422. Return 200 with the summary. Preserve contact, messages, tags, state, assignment, channel, `CreatedAt`, and first-response/first-resolution milestones. Set the inbox, touch `UpdatedAt`, recalculate deadlines from original creation time, and detect newly apparent breaches. Record one transfer-history row for an actual move. Moving to the current inbox is a no-op with no extra history. Do not auto-assign or publish an invented existing webhook type.

**Implementation plan.**

1. Create source/destination inboxes with different SLA targets and a chat thread. Assert transfer preserves its ID and opening message while changing its due time.
2. Add `ConversationTransferEvent` containing ID, conversation ID, actor teammate ID, from/to inbox IDs, and occurred time. Keep historical actor/inbox IDs as raw values, following assignment history; cascade with conversation deletion. Index `(ConversationId, CreatedAt, Id)` and generate `M07ConversationTransfers`.
3. Add a domain move method that changes only inbox ID and update time. Check the channel and destination before invoking it. Return the current summary early for a same-inbox request.
4. Stage the transfer row using claims for the actor. Apply `SlaPolicies.ApplyAsync` with the destination inbox, then `SlaBreaches.DetectAsync`, using one captured time.
5. Save once so the move, history, and breach rows commit together. Preserve existing breach history even if the new deadline is later; it describes an earlier recorded failure.
6. Add GET `api/conversations/{id}/transfer-events`, scoped through the parent and paged oldest first by time then ID. Do not expose a workspace-wide audit endpoint in this story.
7. Test same-inbox repeats, Closed/Snoozed chat threads, assigned threads, earlier/later destination deadlines, email rejection, and foreign destination rejection. A failed transfer must leave history empty.
8. Run SLA/channel regressions and migration checks. Document that subsequent priority changes use the new inbox and that this command deliberately preserves assignment.

**Concrete checks.** A 10:00 conversation moves at 11:00 from a 120-minute inbox to a 30-minute inbox: its new due time is 10:30, and an unanswered target is now breached. It must not become due at 11:30. Repeating the transfer leaves one history row.

**Run:** `dotnet test --filter "FullyQualifiedName~M07|FullyQualifiedName~SlaEngineTests|FullyQualifiedName~ChannelParityTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why calling `ConversationStarter` would create a different conversation rather than move this one.

**Done when:** a chat thread moves without losing identity or history, and its new SLA behavior follows the destination inbox consistently.

<a id="m08"></a>
## M08: Wake a bounded batch of due snoozed conversations

**User story.** As a workspace Admin, I want an explicit command to reopen conversations whose snooze time has arrived so they return to the working queue.

**Learn the codebase.** A persisted future timestamp does not execute work on its own. The current state methods already know how to reopen a conversation; this feature selects when to call them.

**Open first:** [Conversation.Open](../src/Harbor.Domain/Entities/Conversation.cs), [ConversationsController.ChangeState](../src/Harbor.Api/Controllers/ConversationsController.cs), [Program](../src/Harbor.Api/Program.cs), and [ConversationStateMachineTests](../tests/Harbor.Tests/Integration/ConversationStateMachineTests.cs). Create `M08WakeSnoozesTests.cs`.

**Contract and acceptance criteria.** POST `api/workspaces/{workspaceId}/conversations/wake-due?limit=50` is Admin-only. Limit must be 1–200, otherwise 400. Select Snoozed rows with nonnull `SnoozedUntil <= now`, ordered by that timestamp then ID, and reopen at most the limit. Return 200 with changed IDs and `EvaluatedAt`; no matches returns an empty array within that response. Preserve assignment, first milestones, deadlines, and message activity. A second call must not reopen already-open rows. No timer, worker, webhook, or new table.

**Implementation plan.**

1. Arrange snoozes one tick before, exactly at, and one tick after a fixed time. Add an already Open and a Closed conversation.
2. Create a small wake service with `WakeAsync(workspaceId, limit, now, cancellationToken)`. Keep time as an explicit argument so the decision is testable without sleeping.
3. Let the HTTP action obtain time through a registered `TimeProvider` used by this new path. Override it in the test host or test the service's fixed-time boundary separately; do not refactor every existing clock in this story.
4. Query only eligible workspace rows and apply `Take(limit)` in SQL. Call the existing `Open(now)` for each. Do not create messages to make them appear active.
5. Save once. A stale tracked conversation should cause the existing 409 concurrency handling and roll back this batch; do not retry by force-overwriting a teammate's concurrent state change.
6. Return IDs in selection order and the one captured evaluation time. Document that callers can issue another command to drain another batch; there is no promise that all due work fits in one call.
7. Test boundary inclusion, deterministic tied times, limit behavior, repeated calls, empty result, Agent 403, missing authentication, and another workspace. Assert `LastMessageAt` and `FirstResolvedAt` remain unchanged.
8. Run state-machine/concurrency regressions and add an example operational request to the API docs.

**Concrete checks.** With three due rows and limit 2, the first call returns two IDs; the second returns the remaining ID; the third returns none. The future row stays Snoozed. A row exactly at the boundary is due even though SLA breach comparisons elsewhere use strict `>`.

**Run:** `dotnet test --filter "FullyQualifiedName~M08|FullyQualifiedName~ConversationStateMachineTests|FullyQualifiedName~ConcurrencyTests"`.

**Explain it back / common trap.** Explain why changing a snooze filter alone cannot wake anything. A query can find work; a command must perform the transition.

**Done when:** deterministic batches reopen only due work, repeated calls are safe, and no hidden scheduler is required.

<a id="m09"></a>
## M09: Read one combined conversation activity timeline

**User story.** As a teammate investigating a thread, I want messages, assignment changes, and SLA breach records in one chronological feed so I can understand their sequence.

**Learn the codebase.** Harbor stores these facts in separate tables. A read model can combine them without changing the write model or inventing events that were never recorded.

**Open first:** [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), [AssignmentEvent](../src/Harbor.Domain/Entities/AssignmentEvent.cs), [SlaBreachEvent](../src/Harbor.Domain/Entities/SlaBreachEvent.cs), and [Paging](../src/Harbor.Api/Infrastructure/Paging.cs). Create `M09TimelineTests.cs`.

**Contract and acceptance criteria.** GET `api/conversations/{id}/timeline` returns a paged array of typed entries from Messages, AssignmentEvents, and SlaBreachEvents. Both roles may read a scoped thread, including internal notes. Each entry has event ID, source kind, occurred time, and the existing safe response for that source. Use message/assignment `CreatedAt`, and breach `BreachedAt`. Order newest first, then source kind in documented order Message=0, Assignment=1, SlaBreach=2, then ID. Count all three sources. Do not claim to show subject/state changes or M07 transfers; they are outside this slice.

**Implementation plan.**

1. Arrange one conversation with two messages, one assignment event, and one breach at known times. Write a page-size-2 test expecting four entries over two pages.
2. Check the parent by ID + workspace before querying any child table. Define the timeline response with an explicit kind and exactly one nonnull source payload.
3. Project each source into the identical minimal query shape: event ID, integer source kind, and occurred timestamp. Use `Concat` to compose them into one SQL query; `Union` would unnecessarily deduplicate events.
4. Order the composed query using the complete ordering above and pass it to the normal paging helper. Keep response mapping methods outside this database union; inspect generated SQL for the translated union and paging.
5. Split the selected page's IDs by source and load those source details in at most three bounded queries. Reassemble them in the selected key order, not in database retrieval order.
6. Treat source identity as `(kind, id)`; do not assume GUID values across different tables can never coincide. Capture count/page within a read transaction if requiring a consistent response while concurrent inserts happen.
7. Add identical-time, repeated-ID-across-source, empty source, page beyond end, large page, foreign parent, and note visibility tests. The opening message counts even if no later messages exist.
8. Run message/assignment/SLA regressions and document what the timeline cannot reconstruct from the current persisted history.

**Concrete checks.** At 10:00 a message and assignment tie; Message sorts first. A breach detected at 12:00 with `BreachedAt=11:30` appears at 11:30. A message and assignment deliberately sharing an ID remain two entries with distinct kinds.

**Run:** `dotnet test --filter "FullyQualifiedName~M09|FullyQualifiedName~MessagingEndpointsTests|FullyQualifiedName~StateAndAssignmentTests"`.

**Explain it back / common trap.** Explain why merging three separately paged lists does not produce the correct page of a combined timeline.

**Done when:** the global count and ordering work across source boundaries, and no undocumented history is implied.

<a id="m10"></a>
## M10: Explain which teammates are eligible for assignment

**User story.** As a teammate, I want to see who is available and below capacity so I can understand why auto-assignment has few or no candidates.

**Learn the codebase.** `AutoAssigner.PickNextAsync` changes an inbox's rotation pointer. Calling it to answer a read-only question would consume a turn. Separate eligibility facts from selection side effects.

**Open first:** [AutoAssigner](../src/Harbor.Infrastructure/AutoAssigner.cs), [Teammate](../src/Harbor.Domain/Entities/Teammate.cs), [AssignmentRulesTests](../tests/Harbor.Tests/Integration/AssignmentRulesTests.cs), and [AssignmentEdgeCaseTests](../tests/Harbor.Tests/Integration/AssignmentEdgeCaseTests.cs). Create `M10AssignmentEligibilityTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/assignment-eligibility` returns a paged teammate list with ID, name, availability, non-Closed assigned conversation count, capacity limit, eligible boolean, and reason flags `Away` and `AtCapacity`. A teammate can have both reasons. Null capacity means unlimited. Team-only assignments count toward no individual. Both roles may read their workspace. Include the bootstrap Admin like any other teammate. Sort by creation time then ID. This explains candidate eligibility, not who will win an inbox's next rotation.

**Implementation plan.**

1. Arrange Available-under-limit, Available-at-limit, Away-under-limit, and Away-at-limit teammates. Add both Open and Snoozed assignments so tests pin the actual non-Closed rule.
2. Extract a pure eligibility calculation accepting availability, capacity, and assigned count. Return flags plus the boolean derived from them.
3. Add a reusable scoped count query grouped by assigned teammate ID. Use it in the diagnostic path and adapt auto-assignment to the shared rule without changing rotation ordering or pointer updates.
4. Page the scoped directory first, then fetch grouped counts only for those page IDs, or project a SQL-correlated count. Avoid one count query per teammate. Total headers describe the directory count.
5. Map safe diagnostic fields; never return API key hashes. A null limit produces no `AtCapacity` reason regardless of count; zero remaining capacity is not null.
6. Add tests proving repeated GETs leave the inbox pointer and assignments unchanged. Then run a real assignment and verify the existing candidate-selection behavior still holds.
7. Test capacity boundaries, both reason flags together, Closed exclusions, team-only exclusions, bootstrap inclusion, paging, and foreign workspace isolation. Explicitly set bootstrap availability in selection fixtures.
8. Run all assignment regressions and document the distinction between eligibility and manual assignment, which currently has its own rules.

**Concrete checks.** With capacity 2, one Open plus one Snoozed means count 2 and `AtCapacity`. One Open plus one Closed means count 1 and eligible if Available. Away with count 2/limit 2 returns both flags.

**Run:** `dotnet test --filter "FullyQualifiedName~M10|FullyQualifiedName~AssignmentRulesTests|FullyQualifiedName~AssignmentEdgeCaseTests"`.

**Explain it back / common trap.** Explain why an eligible teammate is not necessarily the next selected teammate, and why this endpoint must not call the mutating picker.

**Done when:** diagnostic reasons match real eligibility, assignment tests still pass, and reading diagnostics changes no routing state.

<a id="m11"></a>
## M11: Patch contact attributes without replacing the whole map

**User story.** As a teammate updating customer context, I want to change a few attributes and remove selected keys without resending or accidentally discarding every other attribute.

**Learn the codebase.** Attributes are stored as JSON strings and used by SQL segment rules. A partial update must preserve the map's representation and distinguish an omitted key from a key explicitly set to null.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs), [ContactAttributes](../src/Harbor.Infrastructure/ContactAttributes.cs), [Contact](../src/Harbor.Domain/Entities/Contact.cs), and [SegmentSqlTranslationTests](../tests/Harbor.Tests/Integration/SegmentSqlTranslationTests.cs). Create `M11AttributePatchTests.cs`.

**Contract and acceptance criteria.** GET `api/contacts/{id}/attributes` returns the current map and version. PATCH accepts a required dictionary `Changes` and nonempty `ExpectedVersion`. A string sets/replaces that key; null removes it; absent keys remain. Trim keys, preserve key case and value whitespace, reject blank keys or keys that collide after trimming. Accept at most 50 changes, key length 100, value length 2,000, and final serialized JSON length 10,000 characters. Empty changes are a valid no-op. Shape/size errors give 400, stale version 409, unavailable contact 404. Both roles may use it. Existing full contact PUT remains a replacement operation.

**Implementation plan.**

1. Start with `{plan: pro, region: eu}`. Test changing plan, adding tier, removing region, and retaining a fourth unspecified key.
2. Make `Contact` implement `IHasVersion`, configure its concurrency token, and generate `M11ContactAttributeVersions`. Backfill existing rows with a nonempty initial token; it may be the same initial value across different rows because updates are also scoped by ID. New objects initialize their own tokens.
3. Add a pure map-patch helper: copy the old map, validate normalized incoming keys before applying anything, apply removals/sets, serialize through `ContactAttributes.Write`, and check the final length.
4. Add scoped GET/PATCH actions. Read the token before mutation and compare it with the expected value. Retain EF's original token so races after the comparison also fail.
5. Assign `AttributesJson` only after all validation succeeds. Do not touch contact name, email, external ID, or `LastSeenAt`. An unchanged serialized value may return the same token.
6. Save once and return the persisted map plus post-save version. Use the existing concurrency handler; never retry by merging against a new map without the caller's knowledge.
7. Test trim collisions such as `plan` and ` plan `, null removal of a nonexistent key, whole-map size overflow, stale tabs, foreign access, and preservation of unrelated fields. Query an attribute-based segment after success to prove the JSON is still SQL-readable.
8. Run contact/segment regressions and migration checks. Explain that the new endpoint protects stale clients while legacy full PUT does not gain a client precondition automatically.

**Concrete checks.** Starting `{plan: pro, region: eu, language: en}`, changes `{plan: enterprise, region: null, tier: gold}` result in `{plan: enterprise, language: en, tier: gold}`. A collision or oversized result leaves the original JSON and token unchanged.

**Run:** `dotnet test --filter "FullyQualifiedName~M11|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~SegmentSqlTranslationTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why using `Changes` directly as the entire stored map would accidentally turn PATCH into replacement.

**Done when:** partial changes preserve omitted keys, invalid patches are atomic, and segment queries see the intended updated values.

<a id="m12"></a>
## M12: Find groups of likely duplicate contacts by email

**User story.** As a teammate cleaning customer records, I want to discover contacts that share an email address so I can inspect potential duplicates before deciding what to do.

**Learn the codebase.** Contact email has an index but is not unique. This story provides a read-only investigation tool; it must not turn an observation into an automatic merge.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs), [HarborDbContext contact mapping](../src/Harbor.Infrastructure/HarborDbContext.cs), [Paging](../src/Harbor.Api/Infrastructure/Paging.cs), and [ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs). Create `M12DuplicateContactTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/contacts/duplicate-emails` returns paged `{normalizedEmail, contactCount}` groups with at least two contacts. Normalize with trimmed lowercase comparison, exclude null/blank addresses, sort count descending then email. GET the same path plus `/members?email=...` returns the matching contacts, ordered by name then ID and paged, but returns an empty array if fewer than two matches remain. Both roles may read their workspace. Required nonblank email query input is validated and capped at 320 characters. No schema change, deduplication, contact update, or conversation reassignment.

**Implementation plan.**

1. Arrange two case-varied copies of one email, three of another, a unique email, blank values, and a matching email in a different workspace.
2. Add response/request records for group summaries and member lookup. Keep the normalization rule explicit and shared between the two query builders.
3. Build a workspace-scoped contacts query, exclude blank addresses, project the normalized email expression, group, filter `Count >= 2`, and sort before paging. Keep grouping in SQL.
4. Implement the members lookup using the same normalization expression. Count the matches; if fewer than two, return a correctly paged empty result. Use a read transaction if count and member retrieval must remain consistent within one response.
5. Reuse `ContactResponse` for member rows. Do not load all contacts to group dictionaries in memory or load all group members into each overview row.
6. Check generated SQL and execute against SQLite. Document comparison as the application's trimmed/lowercase matching rule; do not promise a universal email-identity or Unicode equivalence algorithm.
7. Test group ranking, tied counts, member paging, email query encoding, disappearing duplicates, empty workspace, both roles, and cross-workspace exclusion.
8. Run contact/paging regressions and add a walkthrough: find group, request its members, inspect their separate conversation histories.

**Concrete checks.** Three `b@example.test` rows and two `a@example.test` rows produce two groups ordered B then A, with total header 2. The B member endpoint has total 3. A foreign fourth B does not change either count. Removing one of the two A contacts makes its member endpoint empty.

**Run:** `dotnet test --filter "FullyQualifiedName~M12|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~PaginationBoundaryTests"`.

**Explain it back / common trap.** Explain why matching email is evidence of a possible duplicate, not proof that two contact histories should be combined.

**Done when:** users can move from a scoped duplicate group to its exact member rows, with correct counts and no writes.

<a id="m13"></a>
## M13: Summarize a contact's support history

**User story.** As a teammate opening a customer's record, I want a compact support-history summary so I can understand their previous workload before reading individual threads.

**Learn the codebase.** Conversations and messages have a one-to-many relationship. Joining them before counting conversations can multiply totals. Aggregate each level deliberately.

**Open first:** [Contact](../src/Harbor.Domain/Entities/Contact.cs), [ConversationsController.Overview](../src/Harbor.Api/Controllers/ConversationsController.cs), [ReportsController](../src/Harbor.Api/Controllers/ReportsController.cs), and [ReportingTests](../tests/Harbor.Tests/Integration/ReportingTests.cs). Create `M13ContactHistoryTests.cs`.

**Contract and acceptance criteria.** GET `api/contacts/{id}/support-summary` returns TotalConversations, Open, Snoozed, Closed, Replies, Notes, FirstConversationAt, LatestMessageAt, and LatestConversationId. Latest means conversation `LastMessageAt` descending, then ID ascending. Empty history returns numeric zeroes and null timestamps/ID. Count current state; Closed does not mean “ever resolved.” Count message kinds across all the contact's scoped threads. Both roles may read. No paging, filters, message bodies, new tables, or change to contact deletion rules.

**Implementation plan.**

1. Create one contact with Open/Snoozed/Closed threads. Add several messages to one thread so an accidental join would visibly inflate conversation totals.
2. Add the summary DTO with nullable date/ID fields. Check the contact by ID + workspace before running aggregates; an unknown contact is 404, not an empty summary.
3. Build one reusable scoped conversation query for that contact. Group by a constant to obtain total/current-state counts and minimum creation timestamp, with an explicit zero fallback.
4. Aggregate Replies and Notes from Messages whose conversation is in that scoped query. Use message Kind, not AuthorType: teammate replies and teammate notes are different categories.
5. Select the latest conversation in a separate ordered, limited query. Use its `LastMessageAt` as the latest activity value and return its ID without loading the thread body.
6. Execute the related reads in a read transaction if promising internally consistent totals in one response. Do not imply consistency with a separate later conversation-list request.
7. Add no-history, one-opening-message, repeated messages, tied activity, reopened conversation, both roles, and foreign-contact tests. Reopen a previously resolved thread to prove it counts as Open.
8. Run contact/report/message regressions and document that notes contribute to `LatestMessageAt`, matching current conversation behavior.

**Concrete checks.** Three conversations, each with its opening reply, plus two teammate replies and one note produce TotalConversations=3, Replies=5, Notes=1. If states are one each, Open/Snoozed/Closed are 1/1/1 regardless of message count. A new note can change LatestConversationId without changing first response.

**Run:** `dotnet test --filter "FullyQualifiedName~M13|FullyQualifiedName~ReportingTests|FullyQualifiedName~MessagingEndpointsTests"`.

**Explain it back / common trap.** Draw three conversation boxes with six messages beneath them. Explain why counting joined rows gives six messages, not three conversations.

**Done when:** aggregate totals are independent of join multiplicity and the empty/missing distinction is explicit.

<a id="m14"></a>
## M14: Preview segment rules before saving them

**User story.** As a teammate exploring customer segments, I want to preview how many contacts match a proposed rule set and inspect a small sample without creating a saved segment.

**Learn the codebase.** Segment rules compile into expression trees that EF translates into SQL, including `json_extract` for custom attributes. Preview should reuse that exact interpretation.

**Open first:** [SegmentsController](../src/Harbor.Api/Controllers/SegmentsController.cs), [SegmentCompiler](../src/Harbor.Infrastructure/SegmentCompiler.cs), [SegmentRuleSet](../src/Harbor.Domain/SegmentRuleSet.cs), and [SegmentSqlTranslationTests](../tests/Harbor.Tests/Integration/SegmentSqlTranslationTests.cs). Create `M14SegmentPreviewTests.cs`.

**Contract and acceptance criteria.** POST `api/workspaces/{workspaceId}/segments/preview` accepts required Rules and sample limit, default 10, range 1–25. Return 200 `{totalMatches, sample}` with contacts ordered by name then ID. Accept 1–50 nonnull conditions; malformed shape or unknown Match enum gives 400. Unsupported field/operator/value combinations give the same 422 ProblemDetails style as segment creation. Both roles may preview. No saved segment row, no pagination headers, no contact update, and no arbitrary expression language beyond existing rules.

**Implementation plan.**

1. Arrange contacts with plan attributes: two enterprise, one free, one missing. Preview the enterprise equality rule and expect total 2 with a bounded sample.
2. Add a preview-specific request and validate nested nulls, condition count, field presence, Match values, and limit before calling the compiler. Avoid a null-reference 500 for a null rule or condition.
3. Reuse compiler validation and translate `SegmentRuleException` to 422. If extracting a controller validation helper, keep existing create/update behavior covered by regression tests.
4. Build `db.Contacts.AsNoTracking().Where(workspace).Where(compiledRules)`. Count the entire filtered query; separately order and take the sample limit. Use one read transaction if the response guarantees matching count/sample consistency.
5. Map only the selected contacts through existing response mappings. Do not call `.Compile()` on the expression and enumerate contacts; custom JSON extraction is a SQL-only function.
6. Assert the segment table count and all contact JSON values are unchanged after preview. A POST can still be read-only; its body simply carries a structured query.
7. Test All/Any, missing attributes, negative operators, date boundaries, over-limit sample/conditions, invalid rule combinations, empty matches, and cross-workspace exclusion. Check a saved segment with the same valid rules yields the same contact IDs.
8. Run segment SQL and validation regressions, then document the sample's deterministic order and limited size.

**Concrete checks.** Total=2 and limit=1 must return one contact, not total=1. A missing `attributes.plan` matches `NotEquals enterprise` under current rules; preserve that behavior instead of implementing a new interpretation for preview.

**Run:** `dotnet test --filter "FullyQualifiedName~M14|FullyQualifiedName~SegmentCompilerTests|FullyQualifiedName~SegmentSqlTranslationTests"`.

**Explain it back / common trap.** Explain the difference between compiling a segment into a database expression and compiling that expression into a .NET delegate.

**Done when:** preview membership matches saved-segment membership and the operation leaves every persisted row unchanged.

<a id="m15"></a>
## M15: Clone a segment under a new name

**User story.** As a workspace Admin, I want to copy an existing segment's rules into a new named segment so I can experiment without changing a team's working definition.

**Learn the codebase.** Segment membership is computed dynamically. Cloning copies the rule definition, not a stored membership list or an EF entity's identity.

**Open first:** [SegmentsController](../src/Harbor.Api/Controllers/SegmentsController.cs), [Segment](../src/Harbor.Domain/Entities/Segment.cs), [SegmentQueries](../src/Harbor.Infrastructure/SegmentQueries.cs), and [SegmentTests](../tests/Harbor.Tests/Integration/SegmentTests.cs). Create `M15CloneSegmentTests.cs`.

**Contract and acceptance criteria.** POST `api/segments/{id}/clone` accepts a required new name of 1–200 characters after trimming. Admin-only; unavailable source gives 404, invalid name 400, existing exact stored name in that workspace 409. Return 201 with a new segment and Location pointing to its normal GET. Copy the source's current valid rules, assign a new ID and timestamps, and preserve the source. Keep existing case-sensitive name uniqueness semantics; this story does not introduce a new case-insensitive identity rule. No schema, automatic suffix, or historical snapshots.

**Implementation plan.**

1. Create a source matching enterprise contacts. Clone it as “Enterprise experiment” and assert different ID, equal rules, and equal initial membership.
2. Add the clone request and normalize the new name once. Validate raw maximum length consistently with existing contracts, then reject a blank normalized value.
3. Load source by ID + caller workspace. Deserialize via `SegmentQueries.RulesOf`, validate with `SegmentCompiler`, and return 422 if a legacy stored definition cannot be used.
4. Check the destination name against the workspace's existing names. Construct a fresh Segment with only workspace, requested name, reserialized rules, and new timestamps; never attach the source as Added or copy its ID.
5. Save and return `CreatedAtAction` for the existing GetById. Catch the specific workspace/name uniqueness violation as 409, covering a race after the precheck; other database errors must not masquerade as duplicates.
6. Update the clone through the ordinary update endpoint and verify the source's name/rules remain unchanged. Change a contact attribute and verify both definitions are independently evaluated against new data.
7. Add duplicate-name, whitespace-name, invalid stored rules, Agent 403, anonymous 401, foreign source 404, and same name in a different workspace tests.
8. Run segment regressions and document that a network retry can encounter name conflict after a successful first creation; this endpoint is not an idempotent upsert.

**Concrete checks.** Source S1 has rules `plan=enterprise`; clone S2 initially matches the same two contacts. Change S2 to `plan=free`: S1 still matches two enterprise contacts and S2 matches the free contact. Deleting S2 leaves S1 available.

**Run:** `dotnet test --filter "FullyQualifiedName~M15|FullyQualifiedName~SegmentTests|FullyQualifiedName~SegmentSqlTranslationTests"`.

**Explain it back / common trap.** Explain which fields are copied values and which fields must be new identity. Equal rules do not imply the same segment object.

**Done when:** an Admin can create an independent, correctly scoped copy and safely change its definition without affecting the source.

<a id="m16"></a>
## M16: Explain a contact's segment membership condition by condition

**User story.** As a teammate checking a customer segment, I want to see which conditions a contact satisfies so I can understand unexpected membership.

**Learn the codebase.** SQL null behavior and custom JSON extraction are part of the segment definition. An explanation produced by a second hand-written evaluator can disagree with the real membership query.

**Open first:** [SegmentCompiler](../src/Harbor.Infrastructure/SegmentCompiler.cs), [SegmentQueries](../src/Harbor.Infrastructure/SegmentQueries.cs), [SegmentsController](../src/Harbor.Api/Controllers/SegmentsController.cs), and [SegmentSqlTranslationTests](../tests/Harbor.Tests/Integration/SegmentSqlTranslationTests.cs). Create `M16SegmentExplanationTests.cs`.

**Contract and acceptance criteria.** GET `api/segments/{id}/contacts/{contactId}/explanation` returns segment/contact IDs, Match mode, overall Matches, and ordered `{conditionIndex, field, operator, value, matches}` entries. Both roles may read. Scope both resources; either unavailable gives 404. This bounded diagnostic supports at most 50 conditions; larger or invalid stored definitions give 422. Return the configured comparison value, but do not add a dump of every contact attribute. No schema, mutation, or rule-editor frontend.

**Implementation plan.**

1. Create a segment with `email Exists` and `attributes.plan Equals enterprise`, and a contact with email but no plan. Assert condition results true/false and All=false.
2. Load the segment and verify the contact in the same caller workspace. Read rules with `SegmentQueries.RulesOf`; validate shape, Match, and the condition limit before evaluating anything.
3. Build a scoped contact query restricted to the single ID. For each condition, construct a one-condition `SegmentRuleSet` and pass it to `SegmentCompiler.Compile`.
4. Execute each condition via `AnyAsync` against that scoped contact query, in original condition order. Do not compile the expression to a .NET delegate: `JsonExtract` intentionally cannot execute there.
5. Evaluate the complete rule set once with the same compiler for the authoritative overall result. Cross-check in tests that All/Any over individual booleans agrees. Use a read transaction so the contact cannot change between condition observations within the response.
6. Keep queries sequential on the DbContext. Document the maximum of 51 scalar membership queries as a deliberate bounded diagnostic tradeoff; avoid hiding it behind parallel calls on one context.
7. Add All/Any, negative missing-field operators, empty-string presence, nullable dates, equal date boundaries, invalid stored rules, over-limit rules, and foreign contact/segment tests.
8. Run SQL translation regressions and document the exact presence semantics: existing `Exists` means nonnull, so an empty string is still present.

**Concrete checks.** A missing plan yields `NotEquals enterprise=true` and `Exists=false`. An empty email yields `Exists=true`. At an exact date boundary, `Before` and `After` both return false. Explanation and actual saved-segment membership must agree for every fixture.

**Run:** `dotnet test --filter "FullyQualifiedName~M16|FullyQualifiedName~SegmentCompilerTests|FullyQualifiedName~SegmentSqlTranslationTests"`.

**Explain it back / common trap.** Explain why a human-readable explanation must use the same evaluator as the feature it explains, even when a simpler in-memory implementation looks attractive.

**Done when:** every reported condition agrees with SQLite membership, and the endpoint makes its bounded query cost explicit.

<a id="m17"></a>
## M17: Simulate which SLA policy would apply

**User story.** As a workspace Admin configuring support targets, I want to preview the policy and deadlines for an inbox, priority, and creation time before starting a conversation.

**Learn the codebase.** `SlaPolicies.Resolve` already defines precedence: inbox specificity outweighs priority specificity; oldest creation time and then ID break ties. Simulation should expose that decision, not duplicate it.

**Open first:** [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), [SlaPolicy](../src/Harbor.Domain/Entities/SlaPolicy.cs), [Conversation.ApplySlaTargets](../src/Harbor.Domain/Entities/Conversation.cs), and [SlaPolicyResolutionTests](../tests/Harbor.Tests/Unit/SlaPolicyResolutionTests.cs). Create `M17SlaSimulationTests.cs`.

**Contract and acceptance criteria.** POST `api/workspaces/{workspaceId}/sla/simulate` accepts required inbox ID, validated priority, and `CreatedAt`. Admin-only. Return 200 with source `Policy`, `InboxFallback`, or `None`; selected policy ID/name when applicable; target minutes; and calculated due timestamps. Unknown/foreign inbox gives 422. Missing/malformed input gives 400; a timestamp whose target addition overflows gives 422. A matching policy with a null first-response target does not borrow the inbox's first-response target. No conversation, breach, or outbox rows are created.

**Implementation plan.**

1. Arrange workspace-default, priority-only, inbox-only, and inbox+priority policies. Write a table-driven test for the expected winner in each scenario.
2. Add request/response records with nullable target fields and a source discriminator. Use required nullable value-type request fields where necessary to distinguish omitted values from defaults.
3. Load the scoped inbox and all workspace policies with no tracking. Invoke `Resolve`; do not reorder policies differently in the controller.
4. Extract a small shared target-description helper if needed so both `ApplyAsync` and simulation use the same fallback rule. Preserve the resolver's specificity and tie-break behavior with existing tests.
5. Calculate deadlines from the supplied creation instant using the existing domain target calculation or a shared pure function. A temporary domain object must remain untracked. Catch only target arithmetic overflow as 422.
6. Distinguish no matching policy plus no inbox target (`None`) from an inbox fallback. Return null resolution minutes/due time for inbox fallback because the inbox stores only a first-response target.
7. Test same-specificity ties, offsets representing the same instant, one-target policies, no-target fallback, invalid priority, overflow, Agent 403, and foreign inbox. Assert all persistent table counts remain unchanged.
8. Run policy/SLA regressions and document a before/after example that compares the simulation with a newly started conversation under identical inputs.

**Concrete checks.** An inbox-only policy with specificity 2 beats a priority-only policy with specificity 1. CreatedAt 10:00 and a 30-minute target yields 10:30 regardless of request time. A selected resolution-only policy yields no first-response due time even if the inbox has a fallback target.

**Run:** `dotnet test --filter "FullyQualifiedName~M17|FullyQualifiedName~SlaPolicyResolutionTests|FullyQualifiedName~SlaEngineTests"`.

**Explain it back / common trap.** Explain the difference between selecting a policy and merging several policies. Current Harbor selects one; it does not merge missing targets from others.

**Done when:** simulated decisions agree with real target application and produce no persistent side effects.

<a id="m18"></a>
## M18: See unresolved SLA targets due soon

**User story.** As a teammate triaging upcoming work, I want a queue of unresolved targets due within the next chosen interval so I can act before they become late.

**Learn the codebase.** One conversation can have two targets. Returning target rows makes that multiplicity explicit instead of silently choosing one deadline and hiding the other.

**Open first:** [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs), [Conversation](../src/Harbor.Domain/Entities/Conversation.cs), [SlaBoundaryTests](../tests/Harbor.Tests/Unit/SlaBoundaryTests.cs), and [ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs). Create `M18UpcomingTargetsTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/sla/upcoming` accepts the existing conversation filters/paging plus `WithinMinutes`, default 60, range 1–10,080. Both roles may read. Return target rows with conversation ID, subject, inbox ID, kind, due time, and one response-level evaluation-time header. Include only non-Closed conversations, unresolved milestones, and due timestamps in `[now, now + WithinMinutes)`. One conversation may contribute two rows. Sort due time ascending, conversation ID, then kind. Total headers count targets. An unavailable segment remains 422. Already-overdue targets are excluded; use existing breach features for those.

**Implementation plan.**

1. Arrange a conversation with both targets in range, one with its first response already completed, one Closed, and one overdue. Pin a fixed evaluation time.
2. Add a request derived from `ConversationFilterRequest` with the validated interval. Preserve inherited date/sort validation and map all existing filters through `BuildAsync`.
3. Capture one time through an injectable clock for this new path. Restrict the shared conversation query to non-Closed state after applying caller filters; contradictory filters legitimately produce no rows.
4. Project unresolved first-response targets into a minimal target-row query; do the same for resolution. Compare the corresponding milestone, not `ClosedAt`, for completion.
5. `Concat` those identically shaped queries, order the complete target sequence, and call `ToPageAsync`. Perform date comparisons in SQL through the existing UTC converter; do not materialize all conversations.
6. Return the captured evaluation time consistently, for example `X-Evaluated-At` in ISO-8601 UTC. Do not call breach detection or move any deadline during this read.
7. Test exact lower/upper boundaries, equivalent timezone offsets, both targets on one conversation, completed milestones, reopening after first resolution, notes without a first response, shared filter combinations, and cross-workspace isolation.
8. Run SLA/queue regressions and document that the result is a list of obligations, so two rows may link to the same thread.

**Concrete checks.** At 10:00 with 60 minutes, a 10:00 unresolved target is included, 11:00 is excluded, and 09:59 is excluded. A conversation with due times 10:15 and 10:45 contributes two rows and total 2. A recorded first resolution stays completed after reopening.

**Run:** `dotnet test --filter "FullyQualifiedName~M18|FullyQualifiedName~SlaBoundaryTests|FullyQualifiedName~ConversationQueueTests"`.

**Explain it back / common trap.** Explain why target count and conversation count differ, and why a previously completed milestone must not become pending merely because a conversation reopened.

**Done when:** the queue exposes the correct upcoming obligations, with stable target paging and no state changes.

<a id="m19"></a>
## M19: Report the age of the current backlog

**User story.** As a teammate reviewing queue health, I want to see how many unfinished conversations are less than a day old, one to three days old, or at least three days old.

**Learn the codebase.** Existing report filters describe a creation-time cohort. Backlog age adds current state and a single evaluation instant; it is not a historical snapshot of what was open yesterday.

**Open first:** [ReportsController](../src/Harbor.Api/Controllers/ReportsController.cs), [ReportFilterRequest](../src/Harbor.Api/Contracts/Requests.cs), [UtcDateTimeOffsetConverter](../src/Harbor.Infrastructure/UtcDateTimeOffsetConverter.cs), and [ReportingTests](../tests/Harbor.Tests/Integration/ReportingTests.cs). Create `M19BacklogAgeTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/reports/backlog-age` accepts existing report filters and returns EvaluatedAt, Total, Open, Snoozed, Under24Hours, From24To72Hours, and AtLeast72Hours. Both roles may read. Include only current Open/Snoozed rows in the filtered cohort. Age is elapsed UTC time since `CreatedAt`; a future creation timestamp is treated as zero age. Exactly 24 hours enters the middle bucket; exactly 72 enters the oldest. Empty results are all zero. No paging or schema change. Reject `From >= To` for this new endpoint with 400.

**Implementation plan.**

1. Create rows aged 23h59m, 24h, 71h59m, 72h, and a Closed row. Fix evaluation time; avoid `UtcNow` drifting between expectations.
2. Extract the report's scoped filtering into a reusable `IQueryable<Conversation>` builder, preserving existing cohort predicates. Existing reports may continue materializing from it; this aggregate need not load entities or tags.
3. Add the new action and validate its range. Capture `now` once and compute thresholds `now - 24h` and `now - 72h` in application code.
4. Add non-Closed state restriction, then one SQL group-by-constant aggregate with conditional counts using CreatedAt comparisons. Do not subtract two converted DateTimeOffsets inside an untranslated SQL expression.
5. Provide the zero fallback and one evaluation time in the response. Verify Total equals Open+Snoozed and equals the sum of the three age buckets.
6. Add future-date and reopened-thread cases. Reopening does not reset `CreatedAt`, so a five-day-old reopened thread is still at least 72 hours old.
7. Test every existing report filter independently and one combination, tied boundary offsets, empty cohort, invalid range, both roles, and another workspace. Compare counts to hand-selected IDs, not another copy of your implementation.
8. Run all report regressions because the shared filter extraction affects them. Document the difference between cohort filtering and the time at which backlog age is measured.

**Concrete checks.** Four unfinished rows at 23h59m, 24h, 71h59m, and 72h produce buckets 1/2/1 and Total=4. Making the 24h row Closed changes them to 1/1/1. Its first-resolution timestamp is irrelevant to the state filter.

**Run:** `dotnet test --filter "FullyQualifiedName~M19|FullyQualifiedName~ReportingTests"`.

**Explain it back / common trap.** Explain why filtering conversations created last month does not reconstruct last month's backlog. The response still describes their current state.

**Done when:** bucket counts are exhaustive, nonoverlapping, and consistent with the shared report cohort.

<a id="m20"></a>
## M20: Report work assigned directly to each team

**User story.** As a teammate coordinating team queues, I want a report of each team's directly assigned conversations and roster size so I can distinguish shared queue work from individuals' assignments.

**Learn the codebase.** Harbor enforces teammate-or-team assignment, while a teammate may belong to multiple teams. Attributing an individual's work to every membership would double-count it; this report deliberately counts only `AssignedTeamId`.

**Open first:** [ReportsController](../src/Harbor.Api/Controllers/ReportsController.cs), [TeamsController](../src/Harbor.Api/Controllers/TeamsController.cs), [TeamMembership](../src/Harbor.Domain/Entities/TeamMembership.cs), and [TeamAndTeammateEndpointsTests](../tests/Harbor.Tests/Integration/TeamAndTeammateEndpointsTests.cs). Create `M20TeamWorkloadTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/reports/team-workload` accepts the existing report filters plus ordinary paging. Return one row per workspace team with ID, name, MemberCount, AvailableMemberCount, Conversations, Open, Snoozed, and Closed. Include teams with zero matching work. Sort team name then ID; total paging headers count teams, not conversations. Both roles may read. Member availability does not imply capacity eligibility. Direct teammate and completely unassigned conversations do not belong to any row. No automatic redistribution or schema change.

**Implementation plan.**

1. Arrange two teams sharing one teammate, one team-only conversation, one teammate-only conversation, and an empty team. The shared teammate's work must not appear in either team's totals.
2. Add a request combining report criteria with page/pageSize without changing existing report binding. Map it into the shared report query builder; extract that builder if M19 has not already done so.
3. Page the scoped team directory first, preserving zero-work teams. Keep the returned team IDs for bounded follow-up queries.
4. Group filtered conversations by `AssignedTeamId` restricted to those page IDs and calculate current-state counts. Keep this separate from roster joins so membership count cannot multiply conversation count.
5. Query roster member count and Available count for the same team IDs, enforcing same-workspace teammate joins. Combine the two aggregate maps onto the selected team page and fill absent counts with zero.
6. Use a read transaction if a consistent combined directory/roster/workload snapshot is part of the response. Do not describe the sum of row totals as all workspace work.
7. Test shared memberships, multiple members per team, direct teammate assignments, zero work, zero members, unavailable teammates, paging, report filters, and foreign workspace isolation.
8. Run report/team regressions and document attribution with the concrete example below. Explain why this report is different from M10's individual eligibility diagnostic.

**Concrete checks.** Team A has three members and two directly assigned conversations: one Open, one Snoozed. Its row is Members=3, Conversations=2, Open=1, Snoozed=1, Closed=0. A member's separate five conversations do not raise that total to seven. Team B can share that member and still show zero work.

**Run:** `dotnet test --filter "FullyQualifiedName~M20|FullyQualifiedName~ReportingTests|FullyQualifiedName~TeamAndTeammateEndpointsTests"`.

**Explain it back / common trap.** Explain why team membership is not conversation ownership. Show how one person in two teams could inflate a naive joined report.

**Done when:** team queue counts and roster counts remain independent, zero-work teams appear, and attribution is clear to callers.

<a id="m21"></a>
## M21: Download the conversation volume report as CSV

**User story.** As a teammate preparing a support report, I want to download the existing conversation volume series as a CSV file so I can use the same numbers in a spreadsheet.

**Learn the codebase.** JSON and CSV are two representations of the same report. Their cohort, bucket boundaries, and zero-filled gaps must come from one computation, not two similar implementations.

**Open first:** [ReportsController.Volume](../src/Harbor.Api/Controllers/ReportsController.cs), [report response contracts](../src/Harbor.Api/Contracts/Responses.cs), [ReportingTests](../tests/Harbor.Tests/Integration/ReportingTests.cs), and [testing guide](../docs/testing.md). Create `M21VolumeCsvTests.cs`.

**Contract and acceptance criteria.** GET `api/workspaces/{workspaceId}/reports/volume.csv` accepts the same report filters and interval as the JSON report. Both roles may read. Return UTF-8 CSV without BOM, `text/csv; charset=utf-8`, and an attachment filename `conversation-volume.csv`. Columns are exactly `bucket_start_utc,bucket_end_utc,started,closed`; each existing volume point becomes one row, oldest first. Use invariant numbers, UTC ISO-8601 dates, and CRLF line endings. An empty report returns only the header. Preserve the 1,000-bucket cap and 422 for an overly fine interval. Validate undefined interval and reversed input range as 400 on the new route. No raw contact or message export.

**Implementation plan.**

1. Write a fixture with conversations in two separated time buckets. Compare parsed CSV rows with the existing JSON points, including the zero-filled gap.
2. Extract volume calculation and its bucket-count guard into a small shared report builder. Reuse the existing scoped cohort query; leave timing-report statistics outside this extraction.
3. Make the existing JSON action call the builder and run its regression tests before adding CSV. Keep bucket spans based on the cohort's events, including first resolutions outside its creation-time filter window.
4. Add a pure CSV renderer accepting the built report. Render only the fixed typed columns; no user-provided title, body, or filename is part of this export. Keep dates and integers culture-independent.
5. Add the CSV action, shared filter lookup, new-route validation, and file response. Convert report-limit failure into the same ProblemDetails shape before beginning a file response.
6. Test response media type, attachment filename, BOM absence, column order, line endings, and empty-header behavior. Do not compare only a substring such as one conversation count.
7. Add UTC offset, week-boundary, nondefault-culture, invalid interval, bucket-cap, and foreign-workspace cases. Sum exported counts and compare them with JSON report totals.
8. Run report regressions and document that CSV is a bounded aggregate export; it does not export the current paginated conversation list.

**Concrete checks.** JSON points with Started values 2,0,1 produce CSV rows with exactly 2,0,1. A conversation created in the requested cohort and first resolved later must appear in that later resolution bucket in both formats. An empty slice still downloads a usable header row.

**Run:** `dotnet test --filter "FullyQualifiedName~M21|FullyQualifiedName~ReportingTests"`.

**Explain it back / common trap.** Explain why changing the CSV's date window independently would make two apparently equivalent reports disagree.

**Done when:** every exported number and bucket matches the JSON report, and the download has a predictable file contract.

<a id="m22"></a>
## M22: Clone an article into a new draft

**User story.** As a help-center Admin, I want to copy an existing article into a new draft so I can adapt its content without changing the original public page.

**Learn the codebase.** Publication state and identity must be reset on a copy. `PublishedAt` records an article's first publication, so copying it would give a new draft a misleading history.

**Open first:** [HelpCenterController](../src/Harbor.Api/Controllers/HelpCenterController.cs), [Article](../src/Harbor.Domain/Entities/Article.cs), [Slugs](../src/Harbor.Infrastructure/Slugs.cs), and [HelpCenterTests](../tests/Harbor.Tests/Integration/HelpCenterTests.cs). Create `M22ArticleCloneTests.cs`.

**Contract and acceptance criteria.** POST `api/articles/{id}/clone` accepts destination collection ID, required new title up to 300 characters, and optional slug up to 200. Admin-only. Return 201 with a new draft and Location to the normal article GET. Copy only the body; use the requested title/collection and existing slug derivation rules. New ID, CreatedAt, UpdatedAt; Status=Draft and PublishedAt=null. Unknown source gives 404, unavailable destination/unusable slug 422, workspace slug collision 409, invalid shape 400. Sources may be Draft or Published. No automatic publish, revisions, or collection cloning.

**Implementation plan.**

1. Publish a source article, clone it, and assert that the original public URL still works while the clone's public slug returns 404.
2. Add a request record reusing existing title/slug limits. Trace `CreateArticle` for destination validation and slug conflict behavior.
3. Load the source by ID + caller workspace; load the destination within that same workspace. Do not permit a caller to create a copy into a foreign collection.
4. Extract shared article-creation validation if helpful, without invoking the controller from itself. Construct a fresh Article and explicitly copy only Body while setting the new identity/publication fields.
5. Save once and return `CreatedAtAction`. Cover the unique workspace/slug index race by mapping only that known conflict to 409; do not append random suffixes silently.
6. Update the clone through the ordinary edit endpoint and show that the source body stays unchanged. Delete the clone and confirm the original remains.
7. Test published/draft sources, duplicate/unusable slug, default slug derivation, foreign source/destination, role checks, preserved body text, and current timestamps with reasonable captured bounds rather than sleeps.
8. Run help-center/public-authoring regressions and document that retries may receive a slug conflict if the first creation already succeeded.

**Concrete checks.** Source “Refunds” is Published with slug `refunds`. Clone title “Refunds for teams” creates a different ID and slug `refunds-for-teams`, Draft, PublishedAt=null. Editing its body to mention teams must not alter the text returned by the public `refunds` route.

**Run:** `dotnet test --filter "FullyQualifiedName~M22|FullyQualifiedName~HelpCenterTests"`.

**Explain it back / common trap.** List which source fields are reusable content and which describe the source article's identity or lifecycle. Do not use a blanket property copy without checking that distinction.

**Done when:** an independent draft is created with valid routing, while original content and publication behavior remain intact.

<a id="m23"></a>
## M23: Keep article snapshots before edits and publication changes

**User story.** As a help-center Admin, I want to inspect earlier article content and publication state so I can understand what changed before an edit or publish action.

**Learn the codebase.** A useful history record and the change it describes must commit together. This is the same atomicity principle as Harbor's outbox, applied to an internal audit trail.

**Open first:** [HelpCenterController](../src/Harbor.Api/Controllers/HelpCenterController.cs), [Article](../src/Harbor.Domain/Entities/Article.cs), [IHasVersion](../src/Harbor.Domain/IHasVersion.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs). Create `M23ArticleRevisionTests.cs`.

**Contract and acceptance criteria.** Before an actual title/body/slug/collection edit or publication-state transition, save a snapshot of the old article with reason Edit/Publish/Unpublish, actor ID, recorded time, and source version. GET `api/articles/{id}/revisions` returns paged metadata newest first by time then ID; GET `/revisions/{revisionId}` returns one full snapshot. Both new reads are Admin-only. Missing/foreign parent or mismatched revision gives 404. Existing articles begin with no history; their first subsequent change records their previous state. No restore endpoint or revision on creation/no-op. Article deletion cascades its history; this is not an immutable external audit archive.

**Implementation plan.**

1. Test draft creation → body edit → publish → unpublish. Expect three snapshots containing the state before each operation, not three copies of the latest state.
2. Add `ArticleRevision` with article/workspace IDs, actor ID, reason, source version, recorded time, and old title/body/slug/collection/status/published timestamp. Store historical actor/collection IDs as values; configure cascade from Article and index `(ArticleId, RecordedAt, Id)`.
3. Add `IHasVersion` to Article and its EF token mapping. Generate `M23ArticleRevisions`, including a nonempty initial token for old rows. This protects overlapping authoring writes; it does not introduce a stale-browser precondition to existing PUT.
4. Create a snapshot helper that copies fields before mutation and stages one new revision. Invoke it only after validation succeeds, and only when content or publication state really changes.
5. Wire UpdateArticle, PublishArticle, and UnpublishArticle through it. Keep `PublishedAt`'s first-publication behavior and existing response contracts. Save revision and article in one `SaveChangesAsync`.
6. Implement scoped metadata paging and revision detail lookup. Exclude body from list metadata to avoid loading many 100,000-character snapshots just to show history entries.
7. Test rejected slug/collection writes, no-op edits, repeated publish, foreign revision IDs, roles, and a stale tracked writer. After a failed concurrent save, assert neither that writer's revision nor article change committed.
8. Run help-center/concurrency tests and migration upgrade checks with a preexisting published article. Document snapshot-before-change semantics and deletion retention behavior.

**Concrete checks.** Body A edited to B stores snapshot A/Draft. Publishing B stores B/Draft. Unpublishing stores B/Published with the original first publication time. A duplicate-slug edit adds no snapshot. A later GET of the first revision still returns A.

**Run:** `dotnet test --filter "FullyQualifiedName~M23|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~ConcurrencyTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why saving the revision first and the article in a second transaction can leave a history entry for a change that never happened.

**Done when:** snapshots accurately precede successful changes, failed changes add no history, and both fresh and upgraded databases work.

<a id="m24"></a>
## M24: Suggest related articles on a public help-center page

**User story.** As a help-center reader, I want related articles from the same collection so I can continue learning when the current page does not fully answer my question.

**Learn the codebase.** Public queries must enforce Published status independently of authentication. Ranking also filters drafts, but defense in the ranker does not replace scoping the database query.

**Open first:** [PublicHelpCenterController](../src/Harbor.Api/Controllers/PublicHelpCenterController.cs), [ArticleSuggestions](../src/Harbor.Domain/ArticleSuggestions.cs), [Keywords](../src/Harbor.Domain/Keywords.cs), and [ArticleSuggestionTests](../tests/Harbor.Tests/Unit/ArticleSuggestionTests.cs). Create `M24RelatedArticlesTests.cs`.

**Contract and acceptance criteria.** GET `api/public/workspaces/{workspaceId}/articles/{slug}/related?limit=5` is anonymous. Validate limit 1–10, otherwise 400. Resolve a Published source in that workspace; a draft/missing source gives 404. Consider at most the 200 most recently updated other Published articles in the same collection, ordered UpdatedAt descending then ID before taking candidates. Rank by the source's title/body keywords through the existing scorer and return suggested-article summaries. Exclude zero-score matches and the source. No matches returns 200 empty. No all-workspace search, new scoring model, or schema change.

**Implementation plan.**

1. Create one public source, a related published article, a matching draft, a matching article in another collection, and a foreign-workspace match. Only the same-collection published candidate may be returned.
2. Add the route below the existing public article route and validate its limit. Query the source with workspace + slug + Published in one predicate.
3. Extract keywords from the source title/body. Return empty early when the keyword set is empty, with no candidate scan.
4. Fetch the bounded candidate pool using the explicit publication, workspace, collection, and source-exclusion predicates before `Take(200)`.
5. Call `ArticleSuggestions.Rank` and reuse `SuggestedArticleResponse`. Add a final deterministic ID tie-break to the ranker if required, preserving existing score/title ordering and checking its current callers.
6. Document that the 200-candidate cap trades full-catalog recall for bounded work. Apply the limit after ranking candidates, not before scoring the requested five results.
7. Add title/body weighting, equal-score/title ties, source exclusion, unpublished source, empty keywords, over-200 candidate selection, anonymous access, and no-draft-disclosure tests. Public foreign workspaces can legitimately be visited; isolation means results belong only to the workspace in that URL.
8. Run public/help-center/ranker regressions and show an example response containing public title/slug and match explanation without any internal notes or draft content.

**Concrete checks.** Keyword `refund` appearing in a candidate title and body scores 4 under current weights; body-only scores 1. The original article never suggests itself. A perfect match outside the collection or outside the 200-candidate window is deliberately absent.

**Run:** `dotnet test --filter "FullyQualifiedName~M24|FullyQualifiedName~ArticleSuggestionTests|FullyQualifiedName~HelpCenterTests"`.

**Explain it back / common trap.** Explain why taking five candidates before ranking is different from taking the five best scores from a larger bounded candidate pool.

**Done when:** readers receive deterministic related public content, with explicit candidate limits and no draft leakage.

<a id="m25"></a>
## M25: Collect one helpfulness vote per teammate on an article

**User story.** As a teammate reviewing help-center content, I want to mark an article helpful or unhelpful and let editors see totals so they can identify content worth improving.

**Learn the codebase.** This is authenticated staff feedback, including drafts staff can already read. A vote belongs to a teammate and article; counts must not grow every time the same person changes their opinion.

**Open first:** [HelpCenterController](../src/Harbor.Api/Controllers/HelpCenterController.cs), [ClaimsPrincipalExtensions](../src/Harbor.Api/Infrastructure/ClaimsPrincipalExtensions.cs), [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and [HelpCenterTests](../tests/Harbor.Tests/Integration/HelpCenterTests.cs). Create `M25ArticleFeedbackTests.cs`.

**Contract and acceptance criteria.** PUT `api/articles/{id}/my-feedback` accepts required Helpful=true/false and returns 200 with the caller's vote. GET returns that vote or 404 if absent. DELETE returns 204 even when the vote is already absent, after checking the parent. Both roles may manage their own votes. Admin-only GET `/feedback-summary` returns Total, Helpful, Unhelpful, with zero fallback. Missing/foreign article gives 404. A vote is attached to article identity across later edits; there is no per-revision voting, anonymous voting, comment body, or public totals.

**Implementation plan.**

1. Write a test where two teammates vote true/false, then the first changes to false. Total must remain 2 while helpful drops from 1 to 0.
2. Add `ArticleFeedback` with workspace, article, teammate IDs, Helpful, and updated timestamp. Unique `(ArticleId, TeammateId)`; configure cascade from parent article/teammate and explicit workspace ownership. Generate `M25ArticleFeedback`.
3. Define the required boolean request using a nullable value plus required validation so omitted Helpful is not accidentally treated as false.
4. Implement scoped parent lookup and caller-owned vote lookup. Derive teammate ID from claims. Upsert the vote; a later successful write replaces that person's previous opinion.
5. Handle simultaneous first inserts with the unique key: resolve only that known collision by reloading the caller's existing row and applying the request, or return a documented 409 for retry. Choose the 409 approach for this slice to keep conflict handling small.
6. Implement owner GET/DELETE and a single aggregate summary query. Do not return individual voter identities in summary or attach vote state to public article responses.
7. Test repeats, changed opinion, deletion, missing boolean, same-workspace other owners, role separation for summary, foreign article, draft article, and preservation across article edits. Check deleting an article removes its votes.
8. Run help-center/authorization tests, migration checks, and document how the editor should interpret totals as staff opinions across the article's lifetime.

**Concrete checks.** A=true and B=false yields 2/1/1. A=false yields 2/0/2. Delete B yields 1/0/1; repeat deletion keeps that result. Updating an article title must not create another vote or erase current feedback.

**Run:** `dotnet test --filter "FullyQualifiedName~M25|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~AuthorizationTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why the unique relationship belongs in the database even though the API already looks for an existing vote before inserting.

**Done when:** each teammate contributes at most one current vote, and editors see accurate scoped totals without exposing private voter identity.

<a id="m26"></a>
## M26: Preview a canned reply with named placeholders

**User story.** As a teammate composing a response, I want to preview a canned reply with the current contact and conversation details filled in so I can see exactly what text it produces.

**Learn the codebase.** Canned replies currently store plain text. Keep template parsing pure and the database lookup separate so rendering rules can be tested without starting the API.

**Open first:** [CannedRepliesController](../src/Harbor.Api/Controllers/CannedRepliesController.cs), [CannedReply](../src/Harbor.Domain/Entities/CannedReply.cs), [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs), and [TagAndCannedReplyEndpointsTests](../tests/Harbor.Tests/Integration/TagAndCannedReplyEndpointsTests.cs). Create `M26CannedReplyPreviewTests.cs` and a focused renderer unit-test class.

**Contract and acceptance criteria.** POST `api/conversations/{id}/canned-replies/{replyId}/preview` returns 200 `{body, usedTokens}`. Both roles may preview. Scope conversation and canned reply to the caller's workspace; unavailable conversation gives 404 and unavailable related reply gives 422. Allow only `{{contact.name}}`, `{{contact.email}}`, `{{teammate.name}}`, and `{{conversation.subject}}`; trim whitespace inside braces but keep token names case-sensitive. Missing nullable data becomes empty text. Unknown, nested, or unmatched delimiters give 422; output over 20,000 characters gives 422. Plain text remains valid. No persistence, expressions, arbitrary attribute lookup, HTML rendering, or send action in this story.

**Implementation plan.**

1. Write pure examples for literal text, one token, repeated tokens, null email, and unknown token. Define exact expected output including whitespace.
2. Create `CannedReplyRenderer` as a pure helper accepting template plus a small explicit value map or typed context. Return body and distinct used token names sorted ordinally.
3. Implement a single left-to-right scan. Copy literal spans, locate matching delimiters, validate token names against the allowlist, and append their values. Reject unmatched closing delimiters too; this slice has no escape syntax for literal braces.
4. Never rescan substituted values. If a contact's name contains `{{teammate.name}}`, those characters remain literal customer data. Enforce the output length while building the result.
5. Add the scoped endpoint and load only its conversation, contact, caller teammate, and canned reply context. Do not allow a request to substitute a different author or customer.
6. Map rendering failures to 422 ProblemDetails, using the existing domain exception mechanism or an explicit render result. Keep source template text unchanged and do not write a Message.
7. Test malformed/nested delimiters, whitespace around tokens, case mismatch, exact output limit, null fields, foreign IDs, both roles, and repeated previews. Snapshot message/outbox counts and conversation timing to prove the operation is read-only.
8. Run canned-reply/message regressions and document all four tokens with an input/output example. Keep this renderer reusable by M27.

**Concrete checks.** Template `Hi {{ contact.name }}, {{teammate.name}} is checking {{conversation.subject}}.` with Ada, Sam, and “Billing” renders `Hi Ada, Sam is checking Billing.` If Ada's literal name contains template-looking text, it is not expanded a second time.

**Run:** `dotnet test --filter "FullyQualifiedName~M26|FullyQualifiedName~TagAndCannedReplyEndpointsTests"`.

**Explain it back / common trap.** Explain why repeated string replacement can recursively interpret customer data and why scanning the original template once avoids that problem.

**Done when:** previews are deterministic, bounded, isolated, and leave the actual conversation untouched.

<a id="m27"></a>
## M27: Send a rendered canned reply through the normal message workflow

**User story.** As a teammate, I want to send a canned reply with current conversation details so I can respond without manually copying its rendered text.

**Dependency.** Complete M26 first. This story reuses its renderer and scoped rendering context; it does not add a second template implementation.

**Learn the codebase.** A message write updates lifecycle timestamps, may stamp an email message ID, detects SLA breaches, and stages webhook deliveries. Bypassing any of those steps creates a reply that behaves differently from a manually typed reply.

**Open first:** [ConversationsController.AddMessage](../src/Harbor.Api/Controllers/ConversationsController.cs), [Conversation.RegisterMessage](../src/Harbor.Domain/Entities/Conversation.cs), [EmailRendering](../src/Harbor.Infrastructure/EmailRendering.cs), [Webhooks](../src/Harbor.Infrastructure/Webhooks.cs), and [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs). Create `M27SendCannedReplyTests.cs`.

**Contract and acceptance criteria.** POST `api/conversations/{id}/canned-replies/{replyId}/send` returns 201 with an ordinary MessageResponse and existing conversation Location. Both roles may send. Always author a Reply as the authenticated teammate. Render the latest template/context at send time, reject a blank or over-limit result with 422, and use the M26 scope/error rules. An email thread gets the same persisted channel/message-ID behavior as an ordinary teammate reply; no new SMTP transport is introduced. This action is not idempotent: a repeated successful POST creates another message. No preview token, note mode, or client-supplied author.

**Implementation plan.**

1. Add a test comparing a manually entered reply with a canned reply of the same text: channel, author, first-response behavior, and event shape should match.
2. Extract a helper for the existing post-validation message append work. It should take a tracked conversation, a message with validated author fields/body, and one time; stamp channel/ID/time, register activity, add the message, detect breaches, and stage MessageCreated deliveries. It must not save.
3. Keep existing AddMessage's author validation and contact LastSeenAt behavior intact, then delegate its append work to that helper. Run existing messaging/channel tests before adding the new route.
4. Reuse M26's scoped context lookup and pure renderer. Validate the rendered output as actual message content before staging anything.
5. Construct a Teammate/Reply message with `AuthorTeammateId=User.GetTeammateId()`. Pass it through the helper and call SaveChanges once so message, milestone, breach, and outbox changes commit together.
6. Return the saved message via existing mappings. Do not create a fake contact message to make a Closed conversation reopen: current teammate-reply state behavior remains authoritative.
7. Test chat/email, first and later teammate replies, late first response, Closed/Snoozed threads, empty rendered output, foreign reply, actual caller identity, and one MessageCreated delivery per subscribed endpoint. Rendering failure must create no rows or timing changes.
8. Run channel, messaging, SLA, and outbox regressions. Document send-time rendering and the repeated-POST duplicate risk so a future client does not silently auto-retry it.

**Concrete checks.** An unanswered email thread receives one rendered teammate Reply, gets FirstRespondedAt, an email message ID, and one queued delivery for each active MessageCreated subscriber. Previewing it first adds nothing; sending it twice adds two replies. A blank template result adds none.

**Run:** `dotnet test --filter "FullyQualifiedName~M27|FullyQualifiedName~MessagingEndpointsTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~WebhookOutboxTests"`.

**Explain it back / common trap.** Explain why “insert a Message row” is only part of sending a reply in this codebase. Name the other effects that must share its commit.

**Done when:** canned replies follow the real message behavior and the old message endpoint retains its existing semantics.

<a id="m28"></a>
## M28: Dispatch webhooks in bounded batches

**User story.** As a workspace Admin draining an outbox, I want each dispatch request to attempt a limited number of due deliveries so one request cannot process an arbitrarily large backlog.

**Learn the codebase.** A batch limit bounds memory and request work. It does not create a durable worker claim or make HTTP exactly-once. Paused subscriptions must be excluded before taking the batch or they can consume every slot.

**Open first:** [WebhooksController.Dispatch](../src/Harbor.Api/Controllers/WebhooksController.cs), [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs), [WebhookDelivery](../src/Harbor.Domain/Entities/WebhookDelivery.cs), [FakeWebhookSender](../tests/Harbor.Tests/Integration/FakeWebhookSender.cs), and [WebhookOutboxTests](../tests/Harbor.Tests/Integration/WebhookOutboxTests.cs). Create `M28DispatchBatchTests.cs`.

**Contract and acceptance criteria.** Extend the existing POST `api/workspaces/{workspaceId}/webhooks/dispatch` with `limit`, default 50, range 1–200, invalid input 400. Keep Admin-only authorization and the array response, now containing only selected eligible deliveries that were attempted. Eligible means same-workspace Pending, NextAttemptAt <= captured now, and active same-workspace subscription. Order CreatedAt then ID and take the limit in SQL. Paused/future/terminal rows consume no batch slots or attempts. Preserve retry/backoff behavior. Default dispatch is now bounded rather than draining all due work; document that behavior change.

**Implementation plan.**

1. Arrange five active due rows and one older paused row. Dispatch limit 2 and assert exactly two fake-sender calls, neither for the paused subscription.
2. Add validated query binding in the controller. Extend `DispatchAsync` with a batch-size parameter while preserving cancellation-token call sites; locate every caller with `rg "DispatchAsync" src tests`.
3. Move active subscription and workspace eligibility into the query before ordering/Take. Do not materialize every due row and then stop a loop after two attempts.
4. Add ID as the final tie-break. Capture one now for eligibility and attempt bookkeeping, retaining existing success and exponential failure scheduling.
5. Keep sends sequential and save outcome changes through the existing path. This slice does not add parallel HTTP, leases, automatic scheduling, or crash-safe claims.
6. Update affected tests that deliberately assumed unbounded default behavior to request additional batches. Do not weaken their success/failure assertions just to fit the new result length.
7. Test default/max/invalid limits, exact due boundary, failed retry deferred into the future, multiple successive batches, empty result, Agent 403, and cross-workspace exclusion. Assert paused rows retain AttemptCount=0.
8. Run webhook delivery/sender/outbox tests and document remaining semantics: a send can succeed before outcome persistence, so crash or overlapping dispatch may deliver twice. Receiver deduplication remains necessary for side effects.

**Concrete checks.** Five due successes with limit 2 produce attempted counts 2,2,1,0 across four calls. A failed row moves NextAttemptAt forward and is skipped on an immediate second call. Two hundred paused rows before active rows do not starve the active batch.

**Run:** `dotnet test --filter "FullyQualifiedName~M28|FullyQualifiedName~WebhookOutboxTests|FullyQualifiedName~WebhookDeliveryTests|FullyQualifiedName~HttpWebhookSenderTests"`.

**Explain it back / common trap.** Explain the difference between limiting work, preventing simultaneous workers from claiming the same work, and preventing duplicate external effects. This story implements the first.

**Done when:** dispatch work is bounded in SQL, paused rows cannot consume capacity, and existing retry outcomes remain correct.

<a id="m29"></a>
## M29: Redrive a failed webhook without erasing its history

**User story.** As a workspace Admin whose webhook receiver is repaired, I want one fresh delivery attempt for an exhausted delivery while keeping the original failure record for diagnosis.

**Learn the codebase.** Reusing the failed row would erase attempt history. A new delivery can reference the old one, and a database uniqueness constraint can make repeated redrive requests return the same new work.

**Open first:** [WebhookDelivery](../src/Harbor.Domain/Entities/WebhookDelivery.cs), [WebhooksController](../src/Harbor.Api/Controllers/WebhooksController.cs), [Webhooks](../src/Harbor.Infrastructure/Webhooks.cs), [WebhookSender](../src/Harbor.Infrastructure/WebhookSender.cs), and [WebhookTests](../tests/Harbor.Tests/Integration/WebhookTests.cs). Create `M29WebhookRedriveTests.cs`.

**Contract and acceptance criteria.** Admin-only POST `api/webhook-deliveries/{id}/redrive` queues one child of an original Failed delivery. First success returns 201; repeat requests return 200 with that same child, even if it has since succeeded or failed. Pending/Succeeded sources, a redrive child used as a source, or an inactive subscription for a new redrive give 409. Missing/foreign delivery gives 404. Add Admin GET `api/webhook-deliveries/{id}` for Location/detail. Copy payload bytes, subscription, workspace, and event type; child has a new delivery ID, Pending status, zero attempts, and now as CreatedAt/NextAttemptAt. No immediate HTTP send, fan-out, or unlimited redrive chains.

**Implementation plan.**

1. Create an exhausted failed original with recorded error/attempts. Redrive twice and expect one additional row, identical child ID, and an untouched original.
2. Add nullable `OriginalDeliveryId` to WebhookDelivery with a unique index. Existing rows stay null; SQLite permits multiple nulls in this unique index. Store the historical ID as a raw value rather than a self-referencing cascade relationship. Generate `M29WebhookRedrives`.
3. Add scoped detail/response support for the original-reference field without exposing subscription secrets. Load the source through workspace scope and verify its subscription is scoped too.
4. Reject child-as-source; then look for an existing child and return it before checking whether a new redrive is currently allowed. This makes retries stable after subscription state changes.
5. For a new child, require Failed status and active subscription. Copy the exact payload string; initialize a fresh delivery without copying failure outcome fields or version.
6. Save once. On the specific OriginalDeliveryId uniqueness race, discard the failed tracked insertion, reload the existing child, and return 200. Do not call `Webhooks.PublishAsync`, which would construct a new payload and fan out to subscribers.
7. Test repeat/racing requests, failed child rejection as a new source, inactive subscription, foreign access, payload byte equality, and independent dispatch of the child. Assert original AttemptCount, Error, and status never reset.
8. Run webhook/signer/migration checks. Document identity explicitly: the child's HTTP delivery header uses its new ID, while the copied JSON payload retains the original payload ID. Receivers can recognize the same logical payload and avoid repeating already-applied effects.

**Concrete checks.** Original D1 is Failed with five attempts. Redrive creates D2 with zero attempts and OriginalDeliveryId=D1. After D2 succeeds, another redrive of D1 still returns D2; redriving D2 itself gives 409. Original payload text remains byte-for-byte equal in both rows.

**Run:** `dotnet test --filter "FullyQualifiedName~M29|FullyQualifiedName~WebhookTests|FullyQualifiedName~WebhookSignerTests|FullyQualifiedName~WebhookOutboxTests"` plus the schema recipe.

**Explain it back / common trap.** Explain why idempotent redrive creation does not guarantee exactly-once receiver processing, especially when an earlier HTTP attempt may have succeeded before Harbor recorded failure.

**Done when:** redrive creates at most one child per original, preserves history, and clearly separates delivery identity from copied payload identity.

<a id="m30"></a>
## M30: Show a workspace's webhook delivery health

**User story.** As a workspace Admin, I want a compact view of ready, deferred, paused, successful, and exhausted deliveries so I can decide whether to dispatch, wait, or investigate failures.

**Learn the codebase.** “Pending” alone is not enough to explain operational state. Eligibility also depends on retry time and subscription activity; use the same definitions as dispatch.

**Open first:** [WebhooksController](../src/Harbor.Api/Controllers/WebhooksController.cs), [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs), [WebhookDelivery](../src/Harbor.Domain/Entities/WebhookDelivery.cs), and [WebhookOutboxTests](../tests/Harbor.Tests/Integration/WebhookOutboxTests.cs). Create `M30WebhookHealthTests.cs`.

**Contract and acceptance criteria.** Admin-only GET `api/workspaces/{workspaceId}/webhooks/health` returns EvaluatedAt; Total, Pending, Succeeded, Failed; ReadyPending, DeferredPending, PausedPending; OldestReadyAt; LatestDeliveredAt; and nullable TerminalFailureRate. Ready=Pending + active subscription + NextAttemptAt<=now. Deferred=Pending + active + future retry. Paused=Pending + inactive subscription, regardless of due time. OldestReadyAt is minimum NextAttemptAt among ready rows. LatestDeliveredAt is maximum DeliveredAt among successes. Failure rate is Failed/(Failed+Succeeded), rounded to four decimals, or null with no terminal rows. Count delivery records, including redrive children if M29 exists. No payloads, URLs, secrets, error strings, filters, pagination, or writes.

**Implementation plan.**

1. Arrange two ready, one deferred, one paused, three succeeded, and one failed delivery using fixed times. Write all expected counts before implementing the query.
2. Add the response DTO with nullable timestamps/rate. Capture one evaluation time through a clock that the new endpoint's tests can control.
3. Build a workspace-scoped no-tracking delivery query with subscription activity in its projection. Reuse/extract eligibility predicates from dispatch where practical without forcing all outcomes into the same Pending predicate.
4. Compute counts and conditional date extrema in one aggregate SQL query with a zero fallback. Do not download payloads or loop over delivery histories in memory to count statuses.
5. Calculate the ratio from returned integer terminal counts using floating-point division; keep it null rather than zero when there is no denominator. Return the one captured evaluation time.
6. Assert partition invariants: Total=Pending+Succeeded+Failed and Pending=Ready+Deferred+Paused. Keep subscription foreign keys/scoping intact so every normal delivery has a valid scoped parent.
7. Test exact retry boundary, inactive-but-due rows, empty workspace, only-pending history, no successes, both terminal outcomes, foreign workspace, and Agent 403. Check serialized output contains no delivery payload, URL, secret, or error field.
8. Run dispatch/outbox regressions and document that these are current row-state counts, not attempt-level success percentages or historical availability statistics.

**Concrete checks.** The fixture totals 8 deliveries: Pending=4, Succeeded=3, Failed=1; pending partitions are 2/1/1; TerminalFailureRate=0.25. Pausing an active ready subscription moves its pending rows from Ready to Paused without changing Pending or Total. With only pending rows, the rate is null.

**Run:** `dotnet test --filter "FullyQualifiedName~M30|FullyQualifiedName~WebhookOutboxTests|FullyQualifiedName~WebhookDeliveryTests"`.

**Explain it back / common trap.** Explain why a 25% terminal failure rate is not the same as a 25% failed-attempt rate. A successful delivery may have had several earlier failed attempts.

**Done when:** the summary describes actual dispatch eligibility, its partitions add up, and reading it changes no delivery state.

## Finish a feature by teaching it back

Use this checklist after implementing one story, not after merely reading its plan:

- I can trace the feature from its route through scope checks to its query or domain rule and back to its response.
- I can explain the feature once in user language, once using a small fixture, and once by pointing to the relevant code.
- I can name the invariant that crosses files: ownership, atomicity, uniqueness, publication visibility, time boundary, or another rule specified in the story.
- I verified the expected successful response and the specified invalid, empty, unauthorized, foreign-workspace, and conflict cases.
- My tests ran and were discovered; I recorded their actual outcomes rather than assuming a green build proves behavior.
- If I changed the schema, I reviewed the migration and tested existing data through an upgrade, not only `EnsureCreated`.
- I documented the new API and any intentional behavior change, especially retries, batch limits, personal ownership, and what counts mean.
- I can describe one tradeoff honestly, without calling a bounded implementation universally scalable or claiming exactly-once delivery.
- I reviewed my own diff and can justify each changed file. I did not bundle unrelated stories into the same change merely because they touch the same controller.

For repetition, revisit the same feature the next day using [repeat and recall](13-repeat-and-recall.md). Start from a different angle: first trace an error, then a successful database write, then the test fixture. Use [pairing dialogues](12-pairing-dialogues.md) when you need an example of asking precise questions while reading unfamiliar code.

If you get stuck, write: “In M__, step __, I expected __ but observed __. The relevant request is __. The test/code path I traced is __. My current hypothesis is __.” That gives a reviewer something concrete to help with and turns the difficulty into a reusable debugging lesson.
