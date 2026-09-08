# 11: A lost response is not a failed operation

Stories: S01-S02. Implementation and tests are being added; use the [tracker](../progress.md) for current verification. This lesson revisits transactions from the perspective of two clients and a lost network response.

## Tell the story from both sides

The server creates a conversation and commits it. The response is lost before the client receives it. The client sees a timeout and cannot tell whether creation happened. Retrying the ordinary start endpoint can create another conversation. A client-side timeout describes what the client observed, not what the database committed.

S01 lets the client supply an `Idempotency-Key`. The server retains the request's meaning and the exact successful response under that key. A retry with the same meaning returns the stored response. The database contains one creation even if the client receives the result twice.

Draw two timelines, one for the client and one for the database. Mark the commit on the database timeline before the lost response on the network line. The gap between those marks is why storing only a process-local cache or retrying blindly is insufficient.

## Identity and meaning are separate

Read [IngressIdentity.cs](../../../src/Harbor.Api/Infrastructure/IngressIdentity.cs). A chat creation key is scoped by workspace, authenticated teammate, and operation. The same literal key used by two people is two identities. Authentication still runs before replay; possession of a guessed key is not access to a stored response.

The hash records meaning: inbox ID, contact ID, exact subject/body, and the resolved default priority, in a fixed schema. JSON property order is not meaning. Omitting priority and explicitly selecting Normal mean the same thing. Changing the body with the same key is a conflicting request and returns 409.

The key is restricted to 1-128 ASCII letters, digits, hyphens, and underscores. The receipt has a schema version because canonicalization is itself a versioned contract. If a future release changes normalization, it must consider retained receipts rather than silently reinterpret old hashes.

Explain this using a coat-check ticket. The ticket number identifies the retrieval request; the contents associated with it determine what the server owes. Reusing the number to claim a different coat is a conflict. Now use database words: a unique composite index protects operation identity, and a stored request hash checks compatibility with that identity.

## Why check-then-insert is not enough

Without serialization, A and B can both query for a receipt, both find none, and both stage a conversation. A unique receipt index may reject the second insert, but correct handling also needs all creation effects to roll back with it. Reserving SQLite's writer before the decision reads makes the intended order explicit.

Read [WriteTransactions.cs](../../../src/Harbor.Infrastructure/WriteTransactions.cs). It opens a short immediate transaction, enlists EF on that same connection, executes database work, and commits. Known busy/locked failures get at most three attempts with fresh tracked state. Exhaustion maps to 503 and Retry-After: 1 through [WriteBusyExceptionHandler.cs](../../../src/Harbor.Api/Infrastructure/WriteBusyExceptionHandler.cs).

This helper's callback must contain database work only. Retrying a rolled-back SQL operation is different from retrying an HTTP send or a file write that already escaped the transaction. Nested callers reuse an existing transaction so a shared workflow does not accidentally commit part of an outer operation.

The keyed conversation action calls the ordinary scoped start behavior inside this transaction. An intermediate SaveChanges persists the conversation within the still-open transaction; it does not make that work independently committed. The receipt is staged and saved before the outer commit. A later failure rolls back both saves.

## Replaying a response versus rebuilding one

Read `Start` and `Replay` in [ConversationsController.cs](../../../src/Harbor.Api/Controllers/ConversationsController.cs). The receipt stores serialized JSON with the API's configured options, status, content type, and Location. The creation response is bounded to 256 KiB. Only successful creation responses are retained; invalid business input does not consume the key.

Suppose the conversation receives another reply after its creation. Rebuilding a response by reading today's conversation would return a different message list. A retry is asking for the result of the original command, so S01 returns the retained original body. The replay header is `Idempotency-Replayed: true`.

Receipts are retained for at least 24 hours, with no cleanup in this slice. They continue replaying while retained, including after the minimum retention timestamp. That timestamp does not itself delete a row or turn a retry into a new creation. An eventual cleanup policy changes the retry guarantee and must be documented deliberately.

## Email has two different identities

S02 uses the provider's nonblank Message-ID as input identity, scoped by workspace and resolved inbox. Outer whitespace is removed, identifier case is preserved. In-Reply-To and References serve another purpose: identifying an ancestor conversation.

Read the inbound action in [EmailController.cs](../../../src/Harbor.Api/Controllers/EmailController.cs). Receipt lookup happens before creating or touching the sender contact. A matching retry returns the original IDs and created flags without advancing LastSeenAt. A changed canonical input gives 409. Missing or blank MessageId keeps the legacy behavior of treating each ingestion independently.

Canonical email meaning includes normalized From/To addresses, the normalized display name used by ingestion, trimmed identifiers, ordered nonblank References, and exact subject/body. Reference order matters because the nearest known ancestor is useful threading evidence.

Now imagine an unrelated sender referencing a known message. A matching ancestor is not permission to append to that customer's thread. Before writes, the matched conversation must belong to the selected inbox and its actual contact's normalized email must match From. Failure gives 422 and must not leave a newly created sender contact behind.

## A useful refactoring mistake to recognize

M27 extracted shared message staging. That helper generated Harbor email IDs for ordinary API replies. Connecting inbound email to it without an explicit distinction would overwrite the provider's Message-ID and break future thread references.

The inbound caller now passes `preserveInboundEmailIdentity: true` to [MessageAppending.cs](../../../src/Harbor.Api/Infrastructure/MessageAppending.cs). Shared lifecycle effects remain shared, while channel input identity remains intact. Read the test asserting that `<Mail-2>` survives in the stored message. This is a concrete example of why reuse requires checking assumptions, not merely replacing duplicated lines.

## Read and rehearse the failures

Open [S01CreationReplayTests.cs](../../../tests/Harbor.Tests/Integration/S01CreationReplayTests.cs), [S02InboundReceiptTests.cs](../../../tests/Harbor.Tests/Integration/S02InboundReceiptTests.cs), and [IngressTransactionTests.cs](../../../tests/Harbor.Tests/Unit/IngressTransactionTests.cs).

The HTTP tests cover canonical retries, conflicts, unauthenticated replay, invalid keys, inert email replay, provider identity, and wrong sender/inbox. The separate SQLite file test uses independent contexts to compete for one receipt and injects a failure after an intermediate save. That test verifies the transaction helper's persistence behavior; it is not a replacement for complete HTTP concurrency coverage.

[IngressConcurrencyTests.cs](../../../tests/Harbor.Tests/Integration/IngressConcurrencyTests.cs) additionally hosts the API against an isolated migrated SQLite file and releases two authenticated HTTP requests through a gate. It accepts a bounded 503 followed by replay, but never two committed creations. The ordinary factory's shared in-memory connection remains useful for sequential endpoint tests; its optional FileDatabasePath gives these races independent request connections.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~S01|FullyQualifiedName~S02|FullyQualifiedName~IngressTransactionTests|FullyQualifiedName~EmailChannelTests|FullyQualifiedName~MigrationDriftTests'
```

Write four rows in your learner journal: failure before any save, failure after an intermediate save, failure after commit before response, and same-key changed input. For each, predict the number of conversations, messages, receipts, and outbound HTTP sends. In these ingress transactions, outbound HTTP sends should always be zero: webhook publication stages outbox rows instead.

Tomorrow, explain identity versus meaning, save versus commit, and input identity versus thread ancestry without reading this page. Then find the precise query or assertion that supports each explanation. These distinctions recur in imports, background jobs, and scheduled publication later in the bootcamp.
