# Lesson 21: Export jobs and consistent snapshots

S15 turns a bounded conversation export into durable background work. Read [the processor](../../../src/Harbor.Api/Infrastructure/ConversationExports.cs), [HTTP endpoints](../../../src/Harbor.Api/Controllers/ConversationExportsController.cs), [job entity](../../../src/Harbor.Domain/Entities/ConversationExportJob.cs), and [separate-connection snapshot test](../../../tests/Harbor.Tests/Integration/S15ExportSnapshotTests.cs). Verification is recorded in [progress](../progress.md).

## One user action, several moments

An Admin requests an export and receives 202 with a job Location. That means the request was accepted, not that a complete file already exists. A worker later claims the job, reads a database snapshot, writes a private file, and marks the job Ready. Only then is the artifact downloadable.

The file represents data when processing begins. It is not a historical snapshot of enqueue time. This distinction matters when an export waits in a queue while conversations change.

The filter is frozen at enqueue time. If it references a segment, the job captures that segment's rules. Editing the saved segment later does not change the queued job's intended definition. Contact data is still evaluated when the processing snapshot begins.

Repeat this with a concrete example: “I queued the enterprise-customer filter at 09:00. Its rule stays fixed. The file describes matching conversations as observed when processing begins at 09:05.” Both times have a role; neither should be implied by the other.

## Job state and ownership

| State | Meaning | Download behavior |
| --- | --- | --- |
| Queued | Durable request waiting for a worker | 409 |
| Running | One current worker generation has a lease | 409 |
| Ready | Complete artifact published by the owning generation | File |
| Failed | Bounded processing could not finish | 409 with status available separately |
| Expired | Ready retention has ended | 410 |

Claiming happens in a short write transaction. The worker receives a lease token and an increasing generation. The lease lasts five minutes; processing has a two-minute budget. A crash can leave Running work until lease expiry. A later generation may retry, with at most two processing attempts.

The token and generation answer “is this still my job?” before publication. An old worker may finish a file after losing its lease. Its completion must fail rather than replacing the newer worker's result. Each generation writes under a new server-generated artifact ID, so stale output stays private and can be cleaned up.

Compare this with webhook leases. Both reserve ownership before work and fence stale completion. The external work differs: webhooks may already have reached another server, while exports create private artifacts that become visible only through a successful Ready transition.

## Why correct pages can form an incorrect file

Suppose page one reads conversations C1–C250. Another request inserts a new early conversation or changes tags before page two. Independent paged queries may shift membership and ordering. Every query can be individually valid while the combined export duplicates or omits records.

The processor opens one read transaction and establishes the SQLite snapshot before counting rows. It reads all pages within that snapshot, ordered by CreatedAt then ID. A count above 10,000 fails before producing a downloadable artifact. Page size is 250; the count and pages describe the same logical dataset.

The snapshot test creates 251 rows so page two is unavoidable. After the snapshot is established, a separate connection changes the last row and adds another conversation. The export still contains the original last-row value and excludes the later arrival. An ordinary database read afterward sees the committed change. That pair of observations proves the export's read view remained stable.

The test enables SQLite WAL on its disposable file database to allow that writer to commit while the read snapshot remains open. Other journal modes can block or contend with writers. Consistency is not free; measure the operational cost in the deployment's actual configuration.

## Publish a whole file, not its first bytes

The first JSON Lines record is a versioned manifest with snapshot time, filter description, and row count. Subsequent lines are safe conversation summaries. Message bodies are not exported. The manifest gives a reader a way to understand and count the records that follow.

`PrivateFiles.CreateAsync` writes a temporary artifact through a stream that enforces the 50 MiB byte bound and updates a hash. It flushes the completed file and renames it to an immutable blob ID. The job is still not Ready at that point.

The processor releases the read transaction, obtains a short write transaction, and checks lease ownership again. Only a current generation can commit Ready with artifact ID, row count, byte length, hash, snapshot time, and expiry. A crash after rename but before this commit leaves a private orphan, not a partially advertised download.

The ready file expires after 24 hours. Reconciliation marks expiry durably, removes the private file, and clears the artifact reference. Interrupted cleanup can retry. Unreferenced old artifacts and temporary files are eligible for later cleanup; a referenced Ready artifact is protected.

## A failure matrix to fill in yourself

Before running tests, predict the job state and remaining files for each case:

1. Worker stops before claim commits.
2. Worker stops halfway through a temporary file.
3. Worker finishes a file but loses its lease before Ready.
4. Ready commits, then the worker stops before logging success.
5. Job has 10,001 matching conversations.
6. A download is requested exactly at expiry.

Use durable rows and artifact listings to check your answers. Do not use the presence of a temporary file or a log line as proof that the export is ready.

The interrupted-commit case deserves care. If a commit's outcome is uncertain, immediate file deletion could remove a file that a Ready row now references. Keeping the artifact private and letting reference-aware reconciliation decide later is safer than guessing the commit failed.

## Guided practice

Run `S15ConversationExportTests` for the small golden fixture. Read the manifest and count its summaries. Try a paging field in the enqueue request and observe rejection: export membership is a filter contract, not the current screen's page selection.

Run `S15ExportSnapshotTests`. Move the concurrent mutation into a disposable experiment before the snapshot is established, predict the changed output, then restore the original test. This teaches exactly where snapshot timing becomes observable.

Run the lease-expiry test with a frozen clock. Explain why the second generation has a different token and why the first generation cannot publish. Then compare the two-attempt budget with the five-minute lease and two-minute processing budget; they bound different risks.

The worker is disabled by default. Enable `Workers:ConversationExports:Enabled=true` after provisioning durable shared private storage and verifying a disposable job. It scans every 30 seconds and processes one job per pass. Disabling the worker leaves already Ready artifacts available until expiry.

Teach back in product language, then code language: “A job accepts my request; one processing snapshot defines the data; a complete file stays private until the current owner publishes Ready.” Link each clause to one test assertion and record that evidence in the journal.
