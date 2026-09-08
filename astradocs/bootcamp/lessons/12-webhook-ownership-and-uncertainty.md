# 12: Own an attempt before sending it

Stories: S03-S04. See [progress](../progress.md) for verification. This lesson revisits M28-M30 because a dispatcher that works in one request needs stronger rules when two workers run together.

## Draw the four boundaries

The flow is **claim, commit, send, conditionally commit the result**. Write each word in a separate box. The first commit happens before HTTP; the last commit happens afterward. No database transaction remains open during transport.

Previously, two dispatchers could read the same Pending row, both send HTTP, and only then discover a version conflict while saving outcomes. The optimistic token protected the database update, but it could not unsend the earlier requests. This distinction was recorded when the old source comment was corrected.

The new claim records a fresh lease token, a generation number, and an expiry 60 seconds ahead. It also reserves an attempt number and inserts a Started ledger entry in the same transaction. Another worker sees an unexpired owner and cannot claim that row normally.

## Walk through one delivery

Read [WebhookDispatcher.cs](../../../src/Harbor.Infrastructure/WebhookDispatcher.cs), starting at `ClaimNextAsync`.

1. Create a fresh database context and reserve SQLite's writer before reading eligible work.
2. Find one active, due, Pending delivery whose lease is absent or expired. Order by creation time and ID.
3. If an older Started attempt expired, mark that attempt Unknown with a LeaseExpired code.
4. Check the five-attempt budget. An exhausted delivery becomes Failed without another send.
5. Increment the reservation count and generation, create the token/expiry, and insert Started.
6. Save and commit. Return attempt data after the transaction has ended.

The dispatcher claims one row immediately before sending it. It does not claim 50 rows in advance and let the last row's lease age while 49 requests run. A dispatch processes at most 50 reservations, even if an older M28 client supplies a larger valid limit. The HTTP client retains its 10-second timeout.

After transport, `CompleteAsync` uses another fresh context. Completion requires the same delivery, workspace, token, generation, Pending status, and an expiry strictly later than the completion time. The current owner updates the attempt and delivery together, then clears the lease. A stale owner returns false and does not overwrite the newer owner.

## Tell the same story with a schedule

| Time | Worker A | Worker B | Durable state |
| --- | --- | --- | --- |
| 10:00:00 | Claims generation 1 | — | Attempt 1 Started; lease expires 10:01 |
| 10:00:01 | Sending | Tries to claim | B finds no eligible row |
| 10:01:00 | Still paused | Reclaims generation 2 | Attempt 1 Unknown; attempt 2 Started |
| 10:01:01 | Reports old success | — | A fails ownership check; no overwrite |
| 10:01:02 | — | Reports 204 | Attempt 2 Succeeded; delivery Succeeded |

This schedule does not prove that the receiver saw only one request. A may have sent before pausing. B may send after expiry. A lease coordinates temporary ownership in Harbor; receiver-side deduplication is needed when duplicate external effects cannot be tolerated.

## Unknown is a result of missing evidence

Read [WebhookAttempt.cs](../../../src/Harbor.Domain/Entities/WebhookAttempt.cs). Started means an attempt was reserved, not that a receiver accepted it. Succeeded means observed successful HTTP. Rejected means observed non-success HTTP. TransportError means the transport did not return an HTTP result. Unknown means the local system cannot establish the earlier attempt's external result.

The ledger stores IDs, attempt/generation numbers, timestamps, optional status code, and bounded failure codes. It does not store response bodies, secrets, destination URLs, or raw exceptions. The administrator's paged attempt endpoint checks the delivery's scope before exposing these children.

A crash after the receiver accepts but before completion leaves Started. Recovery later changes it to Unknown. Rewriting that history as Rejected would claim evidence the sender does not have. Leaving it permanently Started would hide that recovery had already moved on.

## Count reservations, not only observed results

S04 deliberately changes retry accounting. The budget is consumed when Started is committed. Otherwise repeated crashes could avoid completion bookkeeping and send indefinitely without reaching five attempts. Completion therefore calls the domain success/failure methods with `attemptReserved: true` to avoid incrementing twice.

Recorded failures keep the established 1/2/4/8-minute backoff. An old delivery with AttemptCount 2 but no ledger begins new history at attempt 3. We do not fabricate attempts 1 and 2 from a summary row that lacks their details.

Cancellation before transport conditionally abandons the reservation and consumes its slot. Cancellation after sending may have begun leaves uncertainty for expiry recovery. Pausing a subscription is rechecked before transport where practical, but cannot recall an already in-flight request. These are behavioral limits to explain to an operator, not implementation trivia to hide.

## Read the persistence tests

Open [WebhookLeasePersistenceTests.cs](../../../tests/Harbor.Tests/Unit/WebhookLeasePersistenceTests.cs). Despite its folder, these are real SQLite persistence tests using a disposable file and independent contexts, not mocked database assertions.

The first fake sender reads the database before returning: the lease and Started row must already exist. It pauses with a gate while another dispatcher tries to claim. This tests ownership before transport, which an assertion about final AttemptCount alone could not establish.

The next test advances a fake clock exactly to expiry, reclaims, rejects the old owner's completion, and records a later success. The final test starts from a legacy count of two and crashes through the remaining reservations. It expects only ledger numbers 3, 4, 5 and no sixth reservation.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~WebhookLeasePersistenceTests|FullyQualifiedName~WebhookTests|FullyQualifiedName~WebhookDeliveryTests|FullyQualifiedName~MigrationDriftTests'
```

For practice, add a row to the schedule where A completes one tick before expiry, and another where it completes exactly at expiry. Explain the different ownership result before writing an assertion. Then mark which database commits survive each crash point and which external effects remain unknown.

## Deployment is part of the behavior

Old dispatchers ignore lease fields and use completion-counted attempts. They cannot safely run alongside this accounting scheme. Pause old dispatchers, apply the schema, switch the dispatcher implementation, and then resume. Rolling back needs the same care around active sends and reservations.

Tomorrow's teach-back: explain why a unique attempt key is useful, why a lease is temporary, why Started consumes budget, and why exactly-once HTTP is not promised. Link each explanation to one concrete failure schedule rather than reciting terminology.
