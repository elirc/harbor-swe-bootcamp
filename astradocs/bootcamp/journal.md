# Working journal

## Session 01 — Begin the 75-story implementation

**User priority:** build the requested features while making the upskilling bootcamp the primary output.

**Scope interpretation:** all 25 junior, 30 midlevel, and 20 mid/senior stories. Earlier instructions to leave stories unimplemented were superseded by the explicit request to build all of them. Their original planning documents remain useful specifications.

**Initial observations:** the working tree already contains documentation, shared conversation-query work, tests, and project changes from earlier work. They are preserved. The repository uses .NET 10, ASP.NET Core, EF Core SQLite, and xUnit. The available SDK is `C:/Users/E/.dotnet/dotnet.exe`, version 10.0.400.

**First implementation group:** US01–US05, contact search, presence filters, conversation existence, and name normalization. These features introduce a small shared query that later contact summaries, imports, segments, and merge workflows can build on.

**Baseline verification:** `C:/Users/E/.dotnet/dotnet.exe test --configuration Release --no-restore` passed all 683 existing tests, with no failures or skips. Test execution reported 3 minutes 54 seconds after the initial build. The new contact tests were not part of that compiled baseline. A focused Debug run is checking their pre-implementation failures separately.

**Learning observation:** the first file read encountered a Windows console encoding failure on a BOM in a project file. That is a tooling/output issue, not a C# build failure. Future Python file reads should use UTF-8 output explicitly or handle console encoding before printing.

### First red test run

The focused pre-implementation Debug run discovered 21 contact-learning tests: 7 passed and 14 failed. The failures were expected missing behavior: external-only search did not match, unknown boolean filters were ignored, presence/history queries returned unfiltered rows, and names retained outer spaces. These observations establish that the tests distinguish the new behavior from the old implementation.

### Contact implementation

Added a `ContactFilterRequest`, a scoped `ContactQueries` builder, three optional boolean filters, external-ID search, and outside-only name trimming. Added a SQLite `trim` function mapping with an explicit character set derived once from .NET whitespace classification, because default SQLite trimming only removes ordinary spaces. This is query behavior, not a database schema change; the migration drift checks will verify that distinction.

A Release verification run now covers the new contact examples, existing contact endpoints, pagination boundaries, and migration drift. Result pending. While it runs, the next directory stories (US06-US15) have their initial controller/contract changes staged; their own tests and lesson are still being added. No additional story is marked Verified yet.

### Junior implementation batch: directories and queues

Implemented US06-US25 and added DirectoryLearningTests and QueueLearningTests. The API reference and lessons 02/03 describe the new contracts. Added a spaced practice schedule and a learner journal template.

The first contact/regression verification completed: 78 checks, 77 passed, one failed. The failed assertion expected X-PageSize; the established header is X-Page-Size in Paging.cs. Corrected the test instead of changing the API. SQL whitespace translation and the remaining contact assertions passed in that run. A combined learning/queue/pagination/model verification is running; no green result is claimed yet.

Teaching observation: new tests initially used a nested Teammate.Id and SlaMinutes property that the actual response records do not have. Reviewing Responses.cs showed the flat Id and FirstResponseSlaMinutes fields; corrected those before the next compilation. This is why reading the existing wire contract comes before guessing helper shapes.

### Personal work and versioned editing

Added M01-M04 code: versioned saved filter recipes, owner-scoped bookmarks, private drafts with create-only/null semantics and expected-version edits, and a subject editor using the existing conversation token. New tables need a migration before completion. Added API documentation, lesson 04, HTTP scenarios, and separate-connection persistence tests. Those tests stage overlapping readers deterministically; they are not a claim about all concurrent HTTP schedules.

The combined junior Release test run and the current Debug API build are still running. Other unrelated project builds are active on the machine; none of those processes were changed. No completion or green migration result is claimed.

### Junior verification completed

The Release group filtered to LearningTests, ConversationQueueTests, PaginationBoundaryTests, and MigrationDrift passed 129 tests, zero failures and zero skips (reported test duration 5m04s). It covers the junior implementation snapshot compiled before the later personal-resource schema work. The separate Debug API build completed with zero warnings/errors. US01-US25 are now marked Verified with their lessons and API documentation. Later shared-workflow changes still require regression checks.

Started the M01M02M03PersonalWork migration from the successful Debug model using the locally cached EF 10.0.10 tool. Staged M05/M06/M08/M09/M10 code next: shared priority staging, bounded atomic batch, transactional tag replacement, explicit snooze waking, combined SQL timeline keys, and shared pure assignment eligibility. These newer changes are not yet verified.

### Migration review and full regression started

Generated 20260908190228_M01M02M03PersonalWork. Reviewed Up: it creates only SavedQueues, ConversationBookmarks, ReplyDrafts and their ownership/uniqueness indexes and foreign keys; no existing table or column is dropped. Down removes only those three new tables. The model snapshot was generated with the same EF tool.

Started a full Release test run with a TRX report at .artifacts/bootcamp/bootcamp-current.trx, including all current private-resource and triage tests. The source snapshot is held stable while it builds. Added .artifacts to gitignore so generated test reports stay local. Documentation link validation passed 753 local links across 61 Markdown files before lesson 05 was added; the next documentation check will include that lesson.

### Second-page binding failure

The full Release suite compiled successfully. During execution, M09 first-page timeline assertions passed, but its second-page assertion returned all four entries instead of two. The complex request parameter was named page, colliding with the query-string Page property prefix. Renamed it to paging in SavedQueuesController, PersonalConversationsController, TriageController, TimelineController, ContactInsightsController, and TransfersController. Added a saved-queue second-page regression and explained the observed failure in debugging-guide.md. The existing full run uses the pre-fix assembly; a subsequent run must verify the correction.

After the full run finished compiling, staged M07/M11-M16: chat transfers with history, a Contact version column and bounded attribute PATCH, duplicate email grouping/member reads, support-history aggregates, segment preview/clone/explanation. Started a Debug build for this new model before generating its migration. These changes are not covered by the earlier full-run snapshot.

### Full-run result and confirmed bookmark diagnosis

The first full Release regression finished: 743 total, 741 passed, 2 failed, zero skipped; test duration 8m39s. Failures were the timeline second-page binding issue and bookmark list HTTP 500. A focused no-build diagnostic rerun against the same pre-fix assembly logged the exact EF error: Include was applied after Select projected a different entity through navigation. Changed the bookmark query to include Conversation/Tags on bookmark rows, page those rows, then map their conversations. The diagnostic log is local at .artifacts/bootcamp/m02-before-fix.log.

The next Debug build succeeded with zero warnings/errors. Generated M07M11TransfersAndContactVersions for transfer history and nonempty initial contact version values. Added a real upgrade test that migrates to the previous schema, inserts an old-format contact with explicit old columns, upgrades, then checks token backfill and separate-connection stale-write rejection.

Added M17 SLA simulation and M18 upcoming target rows using shared policy selection and an injectable evaluation clock. A new full Release run is writing .artifacts/bootcamp/bootcamp-through-m18.trx. It includes all current fixes and features through M18; the result is pending.

### Remaining midlevel features staged

After the through-M18 API assembly finished compiling, implemented M19-M30: shared report cohort/volume builders with backlog, team workload, and CSV; article cloning, before-change revisions, staff feedback and bounded related content; a single-pass canned-reply renderer and shared normal-message append staging; bounded webhook dispatch, one-child redrive, and delivery-health aggregation. Added Article version/revision/feedback schema and OriginalDeliveryId uniqueness; their migration is still pending. A Debug API build is running for this model. The through-M18 test run is separate and cannot verify these later edits.

Corrected an existing WebhookDelivery comment that claimed its optimistic token prevents double HTTP delivery. It protects outcome writes; sending can precede the conflict. The later senior lease stories will add durable claims, with external duplicate-effect limitations still stated honestly.

### Midlevel build, migration, and environment failure

The Debug API build through M30 passed with zero warnings and errors. Generated and reviewed 20260908194406_M23M25M29EditorialAndWebhookRecovery: adds the article version with a nonempty backfill default, ArticleRevisions, ArticleFeedback, and nullable uniquely indexed OriginalDeliveryId. Up does not drop existing data. Added an old-schema article upgrade and independent-connection stale-edit rollback test, and included both new tables in the migration schema checks.

The through-M18 full regression encountered a Windows runtime failure while loading Harbor.Domain.dll: `System.IO.FileLoadException`, `An Application Control policy has blocked this file`, code `0x800711C7`. Many affected tests fail while creating the API host, before their behavior assertions. This is not a passing regression result, and it is not evidence that those individual feature assertions failed. No Windows security settings were changed.

Added report, article, renderer/send, and webhook integration tests and lessons 08-10. Started a new full Release build/test for the changed through-M30 source and migration, logging to .artifacts/bootcamp/through-m30.log and bootcamp-through-m30.trx. This verifies a newer source snapshot; its result is pending. The journal retains the earlier failure rather than overwriting it with the next attempt.

### Ingress receipts and a shared-helper assumption

The through-M18 report finished with 752 total, 168 passed, 584 failed, zero skipped. Parsed the TRX: every failed result contains the Application Control loading error, and there are no other failure messages in that report. The through-M30 test assembly compiled and began execution; its result is still pending. Documentation validation passed 827 local links across 68 Markdown files before lesson 11.

Staged S01/S02 durable receipt entities, fixed-schema canonical hashes, immediate SQLite write transactions with bounded busy retries, exact conversation response replay, and inbound receipt metadata. Thread-reference validation now checks the matched inbox and actual sender contact before touching any contact. Added HTTP examples and an independent-connection SQLite file test for competing receipt creation and rollback after an intermediate save. Debug build and receipt migration are pending; no verification is claimed yet.

Code review caught a shared-helper assumption: MessageAppending generated a Harbor email Message-ID for every email reply. Inbound ingestion must preserve the provider's ID. Added an explicit preserveInboundEmailIdentity option and a stored-ID regression assertion. This is documented in lesson 11 as a refactoring boundary, rather than concealed as an unexplained edit.

### Full midlevel regression passed

The through-M30 Release run finished: **777 passed, zero failed, zero skipped**, reported duration 6m42s. This includes the new report/article/template/webhook tests, the old-schema editorial upgrade test, migration drift checks, and the earlier bookmark/paging fixes. The newer compiled artifacts loaded successfully without changing any security setting. The earlier Application Control error remains recorded as an observed environment failure; its underlying cause was not established.

US01-US25 and M01-M30 are now Verified with their API reference and lessons 01-10. S01/S02's separate Debug API build passed with zero warnings/errors (4m08s), and the ingress migration is being generated from that compiled model. S03/S04 code is staged afterward: fresh database contexts for lease claim/completion, 60-second claims, reservation-counted retry budget, and a ledger exposing expired Started attempts as Unknown. Separate-connection tests cover ownership before sender entry, expiry/reclaim, stale completion, and legacy counts without fabricated history. These senior changes are not part of the 777-test verification.

### Credential cutover and migration tooling

The first ingress-only EF generation exited with `Could not load assembly 'Harbor.Infrastructure'`. It did not generate a migration. The reason was not established from that short tool output. Continued with the integrated S01-S05 model rather than claiming an ingress migration existed.

Added the credential table, lifecycle routes, safe metadata, expiry/revocation authentication, and initial credentials in all three production creation paths: workspace bootstrap, teammate creation, and seeding. The compatibility teammate hash column remains, but authentication no longer reads it. Added lifecycle and old-schema backfill tests. The integrated S01-S05 Debug API build passed with zero errors; a verbose EF generation is now logging to .artifacts/bootcamp/s01-s05-migration.log. The credential backfill must be added and reviewed before this group can be verified.

Lessons 12 and 13 explain lease failure schedules and credential cutover. The doc checker passed 866 local links across 71 Markdown files. Code review preserved the existing administrator delivery Error diagnostic while keeping the new ledger free of raw transport exceptions; this maintains the existing webhook regression contract without exposing those details through the new history endpoint.

### Senior migration review and stable verification checkpoint

Generated 20260908201147_S01ToS05DurableIngressDeliveryAndCredentials. Up adds three webhook lease columns and four tables: CreationReceipts, InboundEmailReceipts, TeammateApiKeys, WebhookAttempts, with scoped uniqueness and parent relationships. Added an explicit INSERT SELECT copying each old teammate hash to a nonexpiring initial credential whose ID equals the teammate ID. Existing tables/data are not dropped by Up. Down removes the new state and is not an operationally safe credential rollback after revocation; the lesson documents that limitation.

Added HTTP races using an optional isolated file database on HarborApiFactory, plus a real old-schema authentication upgrade check and cancellation-after-transport ledger recovery. The focused S01/S02/S04/S05 HTTP run is still building/executing and has no reported result yet.

Copied source to .artifacts/checkpoints/s01-s05 for the next full verification, excluding .git, generated bin/obj, test artifacts, and local database files. This keeps the verification source fixed while work continues in the shared workspace. The checkpoint includes S06's preview helper and S07's input validator only; those stories are not integrated or complete. A result against this checkpoint must not be presented as verification of later merge/import/routing changes.

### Contact batches, routing, and watched replies

Started a full Release test from the fixed .artifacts/checkpoints/s01-s05 source, writing checkpoint-s01-s05.log and checkpoint-s01-s05.trx to the main .artifacts/bootcamp directory. Package restore completed; the build/test result is pending.

In the main workspace, integrated S06/S07 stored merge plans and staged imports. Merge preview fingerprints contacts and affected relationships, commit rechecks the plan under the writer reservation, moves conversation/message references, and preserves a replay result. Import staging validates every row without creating contacts; commit creates the whole Ready batch and persists its row mappings together. Validation reads attributes on the positional record's constructor parameters, matching MVC's record contract; TryValidateObject alone would miss those parameter attributes.

Integrated S08/S09 ordered immutable routing versions and personal watched-reply notifications. Both start channels share routing before auto-assignment. Newly added email contacts are saved inside the outer transaction before SQL segment evaluation. Routing decisions record historical version/rule IDs. Notifications are staged through shared existing-thread message append and do not run for opening messages. The S06-S09 Debug build is running; migration, tests, and lessons are still in progress.

The focused senior HTTP run exposed an empty-header fixture issue: the normal HttpClient request with an empty key reached the unkeyed creation behavior (201), while the test expected 400. Replaced that case with an explicit TestServer request containing a present empty header, preserving separate no-header regression coverage. Its verification is pending. The fixed checkpoint retains the earlier fixture and may report this known failure; its source has not been rewritten mid-run. Also corrected an xUnit analyzer warning in the main workspace's race assertion.
# Verification checkpoint: ingress and credentials

The frozen S01–S05 source checkpoint ran 803 tests: 802 passed and one failed. The only failure was `BadKeyDoesNotCreateRows` with an empty string: HttpClient did not transmit a present empty header through that fixture, so it exercised the valid unkeyed creation path. The working tree replaces that case with a direct TestServer request containing a present empty header. That correction remains unverified until the next run. The receipt, credential upgrade, lease, independent-connection race, and other tests in this checkpoint passed. Artifact: `.artifacts/bootcamp/checkpoint-s01-s05.trx`.

S06–S09 compiled successfully; the generated migration adds seven tables and two nullable routing-history columns. It does not drop existing data in Up. S10/S17 also compiled with zero warnings and zero errors. Their migration and new HTTP/upgrade tests are being prepared; a successful build alone does not verify public-content isolation or scheduling behavior.

Learning note: freeze source when running a long suite while continuing work elsewhere. Otherwise a result may refer to different code from the code you are reading. Keep the source checkpoint name alongside its TRX result and distinguish a test-fixture repair from a behavior change.
# Verified checkpoint: S01–S10 and S17

The first publication checkpoint could not compile its tests because ArticleResponse did not expose Version. I added the article token to that authoring response and its mapper in the working tree and the stopped checkpoint, then reran under a distinct artifact name. Result: **825 passed, zero failed/skipped**, full Release suite, 11 minutes 45 seconds of test execution. Artifact: `.artifacts/bootcamp/checkpoint-s01-s10-s17-v2.trx`. This includes the present-empty-header correction, contact merge/import rollback tests, routing, watches, approved public content, scheduled publication, and old-publication migration. It does not verify later S11–S16/S18–S20 source changes.

S11/S12 compiled with zero warnings/errors and received a generated migration. The S18–S20 build initially failed because ChecklistsController lacked the namespace containing NonEmptyGuid; adding that using fixed the error. The subsequent S13/S18–S20 build succeeded with one nullable-value warning, now corrected in source. These are build results, not claims of a completed full regression. Long-running checks are using source checkpoints so later edits cannot silently change what a result refers to.
# Complete-suite rehearsal and contract correction

The first complete current-source Release run executed **854 tests: 853 passed, one failed, zero skipped**. Artifact: `.artifacts/bootcamp/all-stories-v1.trx`. The failure was my new watcher-limit fixture. It arranged 200 conversations watched by one person, but S09's written contract limits one conversation to 200 people. The implementation and existing API documentation used the correct scope. I changed the fixture to 200 people on one conversation, checked the existing owner's idempotent repeat, and checked rejection of person 201. This is a test correction justified by the contract, not a reason to change correct product behavior.

The run also exercised snapshot export under a separate WAL connection, durable stream replay/pruning, attachment rollback/missing-file behavior, configuration audit boundaries, working calendars, recalculation plans, claims, and archival. Those tests passed in this run. Three test-analyzer style warnings were corrected afterward.

The follow-up review added: null-window validation, a forced article version change on actual publication under a fixed clock, captured segment rules in export manifests, and idempotent completion of an already published export artifact. It also found that the older article migration used one initial token for old rows. The publication upgrade now replaces that specific legacy default with each article's unique ID while preserving already changed tokens. A new old-schema test verifies that two legacy drafts have distinct initial versions before scheduling. These follow-up changes require the next run; they are not covered by the 853-pass result above.

Learning content now includes 22 lessons (over 25,000 words), a codebase atlas, test-filter guide, personal notebook template, and the earlier onboarding/practice material. Link verification last checked 1,011 local links successfully before the final guide additions; it will run again after final editing.

## Final current-source verification

The complete Release suite finished successfully: **857 passed, zero failed, zero skipped**, with reported test duration 3 minutes 25 seconds. Command: `dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --configuration Release -m:1 -p:UseSharedCompilation=false --logger "trx;LogFileName=all-stories-v2.trx" --results-directory .artifacts/bootcamp`. This environment used `C:/Users/E/.dotnet/dotnet.exe` because the SDK was not on PATH. Log: `.artifacts/bootcamp/all-stories-v2.log`; structured result: `.artifacts/bootcamp/all-stories-v2.trx`. The build emitted no warnings or errors.

This run includes the corrected per-conversation watcher-limit fixture, distinct legacy article tokens, fixed-clock publication version advancement and scheduled-publication rollback, idempotent export completion, and all earlier story groups. The tracker now records all 75 stories as Verified. That means implemented behavior, executed tests, API documentation, and linked lessons; it does not mean a learner has mastered every lesson or that every possible deployment failure has been simulated.

The finished curriculum has 22 lessons, repeated request/data/failure explanations, exercises and teach-back prompts, a codebase atlas, a focused-test guide, an operating guide, and a practice schedule. Start at lesson 01 and keep your own prediction/result notebook. Returning to an earlier example with a later concept is part of the course, not evidence of falling behind.

## Real-host startup and worker smoke check

Started the compiled Release API in Development on loopback port 51887, using a fresh `.artifacts/bootcamp/final-smoke.db` and separate private storage. Both publication and export workers were enabled for this disposable lab. Startup applied the complete migration chain and seeded demo data. The first HTTP attempt arrived before the listener was ready; it failed to connect, and dependent requests had no workspace ID. After startup completed, reran with terminating request errors and an explicit workspace-ID assertion. This is a useful scripting lesson: a failed setup request must stop the rest of the experiment.

The successful run authenticated the demo administrator, read two seeded public articles, queued an export, observed Ready with four conversation rows, and downloaded it with HTTP 200. It also created a draft, scheduled it for a few seconds ahead, observed Completed through the real periodic worker, and found the approved article in the anonymous public listing. Evidence: `.artifacts/bootcamp/final-smoke.log` and `.artifacts/bootcamp/final-smoke-result.json`. These checks exercise actual host startup and timers in addition to the controlled-time integration tests. Default application configuration still leaves these two workers disabled; the operating guide shows how to enable them.

Final documentation validation checked **1,048 local links across 84 Markdown files with zero broken links**. `git diff --check` passed. All implementation changes remain in the workspace for review; no commit or deployment was created.
