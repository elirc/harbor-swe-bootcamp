# Run a lab and judge its evidence

This is the companion to the [22 lessons](README.md) and [codebase atlas](codebase-atlas.md). The [tracker](progress.md) records which source checkpoint was actually verified. The [journal](journal.md) preserves failures, fixes, and reruns.

## Start with one focused run

From the repository root, run:

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release --filter "FullyQualifiedName~ContactLearningTests"
```

If this machine's SDK is installed in your user profile but is not on PATH, use:

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release --filter "FullyQualifiedName~ContactLearningTests"
```

Read the output in order: did compilation succeed, were tests actually discovered, and how many passed/failed/skipped? A command that discovered zero tests has not verified your feature. Some story tests have descriptive or combined class names rather than a one-story prefix.

## Find the useful test group

Use these names in a FullyQualifiedName filter. Join alternatives with `|` inside the quoted filter.

| Lesson / story group | Useful test names |
| --- | --- |
| 01 / US01–US05 | ContactLearningTests |
| 02 / US06–US15 | DirectoryLearningTests, TagAndCannedReplyEndpointsTests |
| 03 / US16–US25 | QueueLearningTests, ConversationQueueTests |
| 04 / M01–M04 | PersonalWorkLearningTests, SubjectEditingLearningTests, PersonalWorkPersistenceTests |
| 05 / M05–M10 except transfer | TriageLearningTests |
| 06 / M07, M11–M16 | ContactInsightsLearningTests, SegmentLearningTests, ContactUpgradeTests |
| 07 / M17–M18 | SlaLearningTests |
| 08 / M19–M21 | ReportLearningTests |
| 09 / M22–M25 | ArticleLearningTests, EditorialPersistenceTests |
| 10 / M26–M30 | CannedReplyLearningTests, CannedReplyRendererTests, WebhookLearningTests |
| 11 / S01–S02 | S01CreationReplayTests, S02InboundReceiptTests, IngressConcurrencyTests |
| 12 / S03–S04 | WebhookLeasePersistenceTests, S04WebhookAttemptHistoryTests |
| 13 / S05 | S05CredentialLifecycleTests, CredentialUpgradeTests, CredentialUpgradeAuthenticationTests |
| 14 / S06–S07 | S06ContactMergeTests, S07ContactImportTests, ContactWorkflowRollbackTests, SeniorWorkflowConcurrencyTests |
| 15 / S08–S09 | S08RoutingRulesTests, S09WatchedReplyTests, ContactWorkflowRollbackTests |
| 16 / S10 and S17 | S10S17PublicationTests, PublicationUpgradeTests, SeniorWorkflowConcurrencyTests |
| 17 / S11–S12 | WorkingTimeTests, S11S12SlaPlanningTests |
| 18 / S18–S20 | S18S19S20WorkflowTests, SeniorWorkflowConcurrencyTests |
| 19 / S13 | S13AttachmentTests, PrivateFilesTests |
| 20 / S14 | S14WorkspaceEventStreamTests, EventCursorTests |
| 21 / S15 | S15ConversationExportTests, S15ExportSnapshotTests |
| 22 / S16 | S16ConfigurationAuditTests |
| Every schema change | MigrationDriftTests plus the relevant upgrade tests |

Example for publication:

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release --filter "FullyQualifiedName~S10S17PublicationTests|FullyQualifiedName~PublicationUpgradeTests"
```

Do not start by running every possible combination. First use the group that can falsify your current hypothesis. After the implementation is coherent, run the full suite for shared-path regressions.

## Full verification commands

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release
python scripts/check-doc-links.py
git diff --check
```

For a retained test report, choose a unique result name and record which source state it describes:

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release --logger "trx;LogFileName=my-learning-checkpoint.trx" --results-directory .artifacts/bootcamp
```

Build-generated files and disposable verification artifacts belong under ignored output directories. Do not commit API keys, real customer content, local databases, or private file stores as learning fixtures.

## Know what the test proves

A pure calculator test proves arithmetic for its inputs. It does not prove that the email controller calls the calculator. A happy-path HTTP test proves one accepted request. It does not prove two connections cannot race. A migration snapshot comparison proves the model and snapshot agree. An old-schema upgrade test adds evidence that existing rows still have the intended meaning.

For a transaction test, identify the injected failure and inspect rows after it. For a file test, inspect metadata and artifacts separately. For a stream test, use bounded cancellation and read frames rather than waiting for the entire response body to finish. For a snapshot export, mutate through another connection after the snapshot is established.

If a test fails, write a hypothesis before changing code. “The mapping omitted Version” is more useful than “the tests are broken.” Then name the observation that would distinguish a mapping defect from a fixture defect or infrastructure failure.

## Copy this short notebook entry

```text
Story / lesson:
User behavior in one sentence:
Controller and shared helper:
Inputs and rows before the command:
Decision that must use current state:
Commit boundary:
Expected result / expected failure:
Command actually run:
Tests discovered / passed / failed / skipped:
Observed database rows, file state, or frames:
What this evidence does not prove yet:
My next smallest experiment:
Teach-back without looking:
```

A good entry can be ten lines. The purpose is to make your reasoning inspectable, not to produce a long diary for every edit. Revisit it the next day and explain the same feature using a different example. That is how the implementation becomes knowledge you can use independently.
