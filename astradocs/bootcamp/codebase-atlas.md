# Find your way through the implemented codebase

Use this map when a feature feels too large to hold in your head. Pick one row, follow one request, and stop at one commit. You do not need to memorize every controller or table.

Start with the [bootcamp index](README.md), [story tracker](progress.md), and [journal](journal.md). The original story documents explain the intended exercises; the tracker and tests explain current implementation evidence.

## Four questions for any feature

1. **Who is asking?** [Program](../../src/Harbor.Api/Program.cs) installs authentication, authorization, workspace scoping, services, and workers. [API-key authentication](../../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs) resolves a usable credential and current teammate role. The controller still validates related resource ownership.
2. **What decision depends on current state?** Find the query and validation before mutation. Examples are capacity, a reviewed version, staged-upload ownership, or inbox archival.
3. **What commits together?** Follow the helper into staged entities, history, notifications, receipts, and outbox/event rows. [WriteTransactions](../../src/Harbor.Infrastructure/WriteTransactions.cs) reserves SQLite's writer for commands whose decisions require serialized reads.
4. **What proves it?** Open a test with a concrete fixture and inspect its durable-state assertions. For races, find separate connections. For upgrades, find an old schema followed by Migrate. For files, inspect both metadata and artifacts.

## Start at the API, then follow the shared work

| Feature family | Entry points | Shared implementation | First learning stop |
| --- | --- | --- | --- |
| Contact and queue discovery | [Contacts](../../src/Harbor.Api/Controllers/ContactsController.cs), [conversations](../../src/Harbor.Api/Controllers/ConversationsController.cs) | [ContactQueries](../../src/Harbor.Api/Infrastructure/ContactQueries.cs), [ConversationQueries](../../src/Harbor.Api/Infrastructure/ConversationQueries.cs) | Lessons 01–03 |
| Personal queues, bookmarks, drafts | [SavedQueues](../../src/Harbor.Api/Controllers/SavedQueuesController.cs), [personal conversations](../../src/Harbor.Api/Controllers/PersonalConversationsController.cs) | [PersonalWork entities](../../src/Harbor.Domain/Entities/PersonalWork.cs) | Lesson 04 |
| Batches, transfers, timelines | [Triage](../../src/Harbor.Api/Controllers/TriageController.cs), [transfers](../../src/Harbor.Api/Controllers/TransfersController.cs), [timeline](../../src/Harbor.Api/Controllers/TimelineController.cs) | [PriorityChanges](../../src/Harbor.Infrastructure/PriorityChanges.cs), [SnoozeWake](../../src/Harbor.Infrastructure/SnoozeWake.cs) | Lessons 05–06 |
| SLA simulation and migration | [SlaDiagnostics](../../src/Harbor.Api/Controllers/SlaDiagnosticsController.cs), [recalculations](../../src/Harbor.Api/Controllers/SlaRecalculationsController.cs) | [SlaPolicies](../../src/Harbor.Infrastructure/SlaPolicies.cs), [WorkingTime](../../src/Harbor.Domain/WorkingTime.cs) | Lessons 07 and 17 |
| Reports | [Reports](../../src/Harbor.Api/Controllers/ReportsController.cs), [insights](../../src/Harbor.Api/Controllers/ReportInsightsController.cs) | [ReportQueries](../../src/Harbor.Api/Infrastructure/ReportQueries.cs), [VolumeReports](../../src/Harbor.Api/Infrastructure/VolumeReports.cs) | Lesson 08 |
| Articles and publication | [HelpCenter](../../src/Harbor.Api/Controllers/HelpCenterController.cs), [public reads](../../src/Harbor.Api/Controllers/PublicHelpCenterController.cs) | [ArticleHistory](../../src/Harbor.Infrastructure/ArticleHistory.cs), [ArticlePublications](../../src/Harbor.Infrastructure/ArticlePublications.cs), [scheduler](../../src/Harbor.Infrastructure/PublicationScheduler.cs) | Lessons 09 and 16 |
| Canned replies | [CannedReplies](../../src/Harbor.Api/Controllers/CannedRepliesController.cs), [sending](../../src/Harbor.Api/Controllers/CannedReplySendingController.cs) | [renderer](../../src/Harbor.Domain/CannedReplyRenderer.cs), [MessageAppending](../../src/Harbor.Api/Infrastructure/MessageAppending.cs) | Lesson 10 |
| Retry-safe ingress | [Conversation start](../../src/Harbor.Api/Controllers/ConversationsController.cs), [inbound email](../../src/Harbor.Api/Controllers/EmailController.cs) | [IngressIdentity](../../src/Harbor.Api/Infrastructure/IngressIdentity.cs), [receipts](../../src/Harbor.Domain/Entities/IngressReceipts.cs), [starter](../../src/Harbor.Api/Infrastructure/ConversationStarter.cs) | Lesson 11 |
| Webhook delivery | [Webhook operations](../../src/Harbor.Api/Controllers/WebhookOperationsController.cs) | [WebhookDispatcher](../../src/Harbor.Infrastructure/WebhookDispatcher.cs), [attempt ledger](../../src/Harbor.Domain/Entities/WebhookAttempt.cs) | Lesson 12 |
| Credential lifecycle | [ApiKeys](../../src/Harbor.Api/Controllers/ApiKeysController.cs) | [credential rows](../../src/Harbor.Domain/Entities/TeammateApiKey.cs), [ApiKeys](../../src/Harbor.Infrastructure/ApiKeys.cs) | Lesson 13 |
| Reviewed contact changes | [merges](../../src/Harbor.Api/Controllers/ContactMergesController.cs), [imports](../../src/Harbor.Api/Controllers/ContactImportsController.cs) | [merge preview](../../src/Harbor.Infrastructure/ContactMergePreview.cs), [import validation](../../src/Harbor.Api/Infrastructure/ContactImportValidation.cs) | Lesson 14 |
| Routing and watching | [routing](../../src/Harbor.Api/Controllers/InboxRoutingController.cs), [watches](../../src/Harbor.Api/Controllers/ConversationWatchesController.cs) | [InboxRouting](../../src/Harbor.Infrastructure/InboxRouting.cs), [ReplyNotifications](../../src/Harbor.Infrastructure/ReplyNotifications.cs) | Lesson 15 |
| Claims and lifecycle guards | [claim next](../../src/Harbor.Api/Controllers/ClaimNextController.cs), [checklists](../../src/Harbor.Api/Controllers/ChecklistsController.cs), [archive](../../src/Harbor.Api/Controllers/InboxArchiveController.cs) | [ManualAssignments](../../src/Harbor.Api/Infrastructure/ManualAssignments.cs), [InboxLifecycle](../../src/Harbor.Infrastructure/InboxLifecycle.cs) | Lesson 18 |
| Attachments | [Attachments](../../src/Harbor.Api/Controllers/AttachmentsController.cs) | [PrivateFiles](../../src/Harbor.Infrastructure/PrivateFiles.cs), [reconciliation](../../src/Harbor.Infrastructure/AttachmentReconciliation.cs) | Lesson 19 |
| Event stream | [WorkspaceEvents controller](../../src/Harbor.Api/Controllers/WorkspaceEventsController.cs) | [event staging](../../src/Harbor.Infrastructure/WorkspaceEvents.cs), [EventCursors](../../src/Harbor.Api/Infrastructure/EventCursors.cs) | Lesson 20 |
| Exports | [ConversationExports controller](../../src/Harbor.Api/Controllers/ConversationExportsController.cs) | [processor](../../src/Harbor.Api/Infrastructure/ConversationExports.cs), [PrivateFiles](../../src/Harbor.Infrastructure/PrivateFiles.cs) | Lesson 21 |
| Configuration audits | [audit reader](../../src/Harbor.Api/Controllers/ConfigurationAuditController.cs) | [typed audit helpers](../../src/Harbor.Infrastructure/ConfigurationAudits.cs) | Lesson 22 |

All source links above are relative to this document's location. If you are reading this in an editor, use Go to Definition after the first file instead of opening every linked file at once.

## The same write, explained three ways

Use a contact reply on a watched conversation.

**User explanation:** a customer replies; the conversation becomes active again if it was closed; watching teammates receive notifications; connected clients can later observe the new event.

**Code explanation:** the message endpoint or inbound-email controller validates scope and author, then calls MessageAppending. That helper updates conversation lifecycle state and stages the message, private notifications, applicable SLA breaches, webhook deliveries, and workspace events. The caller saves within the command's transaction.

**Failure explanation:** if SQL fails, those staged effects do not commit independently. If webhook transport later fails, the message remains committed and delivery retries use their own lease/attempt history. If a stream disconnects, the durable event remains available for replay. Different failure boundaries require different recovery mechanisms.

Now repeat that exercise for publication. The manual endpoint and scheduler share ArticlePublications. Public readers follow active snapshots. A SQL failure keeps the old approved version. The worker's timer is not the durable schedule; the schedule row is.

## Choose the correct test environment

| Claim you want to make | Minimum useful evidence |
| --- | --- |
| A filter selects the right rows | Mixed included/excluded HTTP fixture and paging assertions |
| A pure deadline calculation is correct | Exact boundary and fractional-tick unit tests |
| A command is atomic | Failure injected after an earlier write, then fresh database assertions |
| Competing writers cannot violate an invariant | Independent connections, concurrent start, and final-state assertions |
| An upgrade preserves old behavior | Old-schema data, real migrations, and before/after checks |
| A file is published safely | Metadata plus file assertions at rename/commit/expiry boundaries |
| A stream resumes safely | Bounded streaming client, scoped ordering, cursor and pruning tests |

The [test factory](../../tests/Harbor.Tests/Integration/HarborApiFactory.cs) defaults to an in-memory SQLite database. FileDatabasePath creates an isolated migrated file database for real connection races. FrozenTime supports time-boundary assertions. FailCommand supports SQL rollback rehearsals. PrivateStorageRoot isolates file tests. Understand each seam before using it; a mock or shared connection cannot prove every concurrency claim.

## A repeatable 25-minute session

Spend five minutes restating one acceptance criterion in plain language. Spend five tracing its controller and helper. Spend five predicting rows before and after one commit. Spend five running or reading the test that checks that prediction. Spend the final five teaching it back and writing one unresolved question.

Tomorrow, start with the unresolved question. Repeating the same feature through its product behavior, data model, and failure timeline builds recall without requiring you to memorize filenames.
