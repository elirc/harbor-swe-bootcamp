# 15: A desk reference for “where do I look?”

**Remember today:** choose a question, then a source file and a nearby test.

## First five lookups

| My question | Open |
| --- | --- |
| How does the application start? | [Program.cs](../src/Harbor.Api/Program.cs) |
| What happens on this conversation request? | [ConversationsController.cs](../src/Harbor.Api/Controllers/ConversationsController.cs) |
| How does conversation state change? | [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs) |
| Which conversations match these filters? | [ConversationQueries.cs](../src/Harbor.Api/Infrastructure/ConversationQueries.cs) |
| Where are queue results tested? | [ConversationQueueTests.cs](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs) |

## Follow a specific feature

| Feature | Source starting point | Evidence starting point |
| --- | --- | --- |
| Creation shared by chat and email | [ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs) | [ChannelParityTests](../tests/Harbor.Tests/Integration/ChannelParityTests.cs) |
| Page bounds and metadata | [Paging](../src/Harbor.Api/Infrastructure/Paging.cs) | [PaginationBoundaryTests](../tests/Harbor.Tests/Integration/PaginationBoundaryTests.cs) |
| API-key identity | [ApiKeyAuthenticationHandler](../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs) | [AuthorizationTests](../tests/Harbor.Tests/Integration/AuthorizationTests.cs) |
| Workspace route access | [WorkspaceScopeFilter](../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs) | [WorkspaceScopeMatrixTests](../tests/Harbor.Tests/Integration/WorkspaceScopeMatrixTests.cs) |
| Teammate selection | [AutoAssigner](../src/Harbor.Infrastructure/AutoAssigner.cs) | [AssignmentRulesTests](../tests/Harbor.Tests/Integration/AssignmentRulesTests.cs) |
| Service targets and breaches | [SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs), [SlaBreaches](../src/Harbor.Infrastructure/SlaBreaches.cs) | [SlaEngineTests](../tests/Harbor.Tests/Integration/SlaEngineTests.cs) |
| Email ingestion and threading | [EmailController](../src/Harbor.Api/Controllers/EmailController.cs) | [EmailChannelTests](../tests/Harbor.Tests/Integration/EmailChannelTests.cs) |
| Queueing webhook deliveries | [Webhooks](../src/Harbor.Infrastructure/Webhooks.cs) | [WebhookOutboxTests](../tests/Harbor.Tests/Integration/WebhookOutboxTests.cs) |
| Sending and retrying webhooks | [WebhookDispatcher](../src/Harbor.Infrastructure/WebhookDispatcher.cs) | [WebhookTests](../tests/Harbor.Tests/Integration/WebhookTests.cs) |
| Segment membership in SQL | [SegmentQueries](../src/Harbor.Infrastructure/SegmentQueries.cs), [SegmentCompiler](../src/Harbor.Infrastructure/SegmentCompiler.cs) | [SegmentSqlTranslationTests](../tests/Harbor.Tests/Integration/SegmentSqlTranslationTests.cs) |
| Articles and public visibility | [PublicHelpCenterController](../src/Harbor.Api/Controllers/PublicHelpCenterController.cs) | [HelpCenterTests](../tests/Harbor.Tests/Integration/HelpCenterTests.cs) |
| Reports | [ReportsController](../src/Harbor.Api/Controllers/ReportsController.cs) | [ReportingTests](../tests/Harbor.Tests/Integration/ReportingTests.cs) |
| Database relationships and versions | [HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs) | [ConcurrencyTests](../tests/Harbor.Tests/Integration/ConcurrencyTests.cs) |
| Schema/model agreement | [Migrations folder](../src/Harbor.Infrastructure/Migrations/) | [MigrationDriftTests](../tests/Harbor.Tests/Unit/MigrationDriftTests.cs) |
| Test application setup | [HarborApiFactory](../tests/Harbor.Tests/Integration/HarborApiFactory.cs) | [Testing reference](../docs/testing.md) |

These are starting points, not an instruction to read every file today. Follow
the next call when your question requires it.

## Translate the words

| Word | Short meaning in this repository |
| --- | --- |
| Endpoint | An HTTP method and route exposing an operation |
| Controller action | The method handling that operation |
| Contract | Input/output behavior a caller can rely on |
| Entity | A business object mapped to persisted data here |
| Domain | Business concepts and behavior |
| Infrastructure | Database and integration capabilities |
| DbContext | EF's access and change-tracking object for database work |
| Query | A description of which stored data to select |
| Materialize | Load query results into application objects |
| Tenant | One organization's partition of data; a workspace here |
| Fixture | A prepared environment/data setup used by tests |
| Assertion | A check comparing an observed outcome with an expectation |
| Migration | A recorded database schema change |
| SLA | A service target measured against a deadline |
| Outbox | Stored work waiting to be sent to another system |
| Concurrency token | A value used to detect certain overlapping stale writes |

Use [the fuller glossary](../docs/learning/glossary.md) when needed. Associate
each new word with a method you have actually inspected.

## Five distinctions near your editor

- A conversation contains messages; it is not one message.
- A query describes selection; a loaded list contains results.
- `Add` prepares an insert; a successful save persists changes.
- A page is part of the matching set; its length need not be the total.
- A queued webhook is stored work; it is not proof of delivery.

## Present capabilities versus future exercises

The code supports filtering, sorting, queue overview, manual SLA evaluation,
manually triggered webhook dispatch (bounded leased claims), an admin-triggered
`wake-due` snooze sweep in `TriageController`, and paged thread reads at
`GET /api/conversations/{id}/messages`. Two hosted workers run in the background:
`PublicationScheduleWorker` and `ConversationExportWorker` (registered in
`Program.cs`). It does not include a front-end chat UI, a periodic snooze sweep,
or a hosted webhook-dispatch loop.

Conversation detail (`GetById`) still includes the whole thread. Stale-edit
protection across separate requests exists only where a request carries an
`ExpectedVersion` (for example `PUT /api/conversations/{id}/subject`, which returns
409 when stale); other updates do not check what the client last saw. The
[capstones](../docs/learning/capstones.md) page marks which exercises now have a
reference implementation.

When lost, return to [the start page](README.md), pick one operation, and follow
one call. You can understand a useful part before understanding all of it.
