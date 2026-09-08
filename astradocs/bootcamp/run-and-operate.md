# Run a disposable learning environment

Use a separate database and private storage directory while experimenting. The commands below assume PowerShell in the repository root. They create lab artifacts under the ignored `.artifacts` directory and leave your usual application database separate.

```powershell
$harborLabRoot = Join-Path (Get-Location).Path '.artifacts/bootcamp'
New-Item -ItemType Directory -Path $harborLabRoot -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Harbor = "Data Source=$harborLabRoot/lab.db"
$env:Storage__PrivateRoot = Join-Path $harborLabRoot 'private-lab'
dotnet run --project src/Harbor.Api --no-launch-profile --urls http://127.0.0.1:5088
```

Use the SDK-path alternative in the [verification guide](verification-guide.md) if `dotnet` is not on PATH. Development startup applies migrations and runs the idempotent demo seeder. Read the startup output before sending requests. A build success is not evidence that startup or migration succeeded.

Open a second terminal for API requests. Follow the repository's [API reference](../../docs/api-reference.md) to create a workspace and retain its returned API key in that terminal's request headers. Start with a contact, inbox, and conversation before trying the later workflow lessons. Do not paste real customer data into your learning fixtures.

## Enable workers deliberately

The API routes and durable job rows exist with workers disabled. To practice automatic scheduled publication and export processing, set these in the application terminal before starting it:

```powershell
$env:Workers__PublicationSchedules__Enabled = 'true'
$env:Workers__ConversationExports__Enabled = 'true'
```

Publication scans every 30 seconds and processes at most 20 due schedules per pass. Export processing scans every 30 seconds and handles one job per pass. Tests call the processing services directly with controlled time; they do not wait for these timers.

For a publication lab, create a draft, fetch its current version, schedule it within 90 days, and inspect Pending/Completed/NeedsReview. Editing the draft after scheduling invalidates the old approval. For an export lab, enqueue a filter, inspect Queued/Running/Ready, download the JSONL manifest and summaries, then compare the reported count.

Stop the application with Ctrl+C when you finish. The lab files persist so you can practice restart behavior. Do not remove a database or private file directory while the application is using it.

## Keep each operational boundary visible

| Resource | Durable source of truth | Recovery entry point |
| --- | --- | --- |
| Creation retry | Stored successful receipt | Retry with the same key and equivalent input |
| Inbound email retry | Scoped provider Message-ID receipt | Replay equivalent inbound input |
| Webhook send | Delivery lease plus attempt ledger | Bounded dispatch and attempt inspection |
| Scheduled publication | Schedule state and reviewed article version | Next enabled pass or Admin review |
| Staged attachment | Metadata plus private blob | Attachment reconcile, maximum 100 artifacts |
| Workspace stream | Event log plus purged-through watermark | Resume cursor or refresh after 410 |
| Export | Job lease/generation plus completed private artifact | Lease recovery and export reconcile |

Webhook dispatch remains an explicit Admin command. A webhook lease does not make the receiving server's effect exactly once. A failed publication schedule is not fixed by ignoring its reviewed version. A missing attachment blob is not fixed by returning an empty download.

## Upgrade and rollback questions

Before an upgrade, ask which old rows need meaningful new values. Credential migration converts legacy hashes into individually revocable rows. Publication migration gives existing public content a snapshot and normalizes untouched legacy article tokens. Existing inboxes start active; existing policies keep elapsed-time behavior unless a calendar is selected.

After the upgrade, verify behavior as well as schema. Can an old key authenticate and then stay revoked? Does an old public article have the same body, URL, collection, and first-publication timestamp? Can an archived inbox reject new starts while accepting validated old-thread replies?

Some old binaries would bypass new invariants. An old article reader could expose private edits. An old close endpoint could ignore required checklist items. An old ingress path could ignore ArchivedAt. Rolling back code therefore needs a deliberate compatibility plan; it does not automatically undo data or external effects.

Instances sharing a database must share durable private blob storage and the Data Protection key ring. The default private root is outside the web root. Keep protection keys across restarts so resume cursors remain readable. Reconcile only through the scoped commands while learning; those commands implement the lifecycle and retention rules explained in the lessons.

Finish a lab by recording its expected rows, observed state, restart behavior, and one recovery action in your notebook. That makes “I ran it” into evidence that you understand how it behaves.
