# Lesson 16: Approved content and durable schedules

Stories S10 and S17 share one important question: **which exact content did the Admin approve?** Implementation and verification status live in the [tracker](../progress.md). Read this lesson with the [publication service](../../../src/Harbor.Infrastructure/ArticlePublications.cs), [scheduler](../../../src/Harbor.Infrastructure/PublicationScheduler.cs), and [HTTP tests](../../../tests/Harbor.Tests/Integration/S10S17PublicationTests.cs) open.

## First explanation: the user's experience

You publish a refund guide containing body A. Later you rewrite it as B. You should be able to save B, take a break, and ask a colleague to review it while customers still read A. Saving and publishing therefore need different meanings.

The editorial Article stores your current work. An immutable publication snapshot stores approved text. A small active-publication row selects the snapshot customers can currently read. Publishing creates a complete snapshot and changes that selection in one database transaction.

Try saying that with no database terms: “I keep working on my copy while readers keep seeing the approved copy. The publish button replaces their copy all at once.” That is the product requirement. The extra tables exist to honor it.

## Second explanation: three rows with different jobs

| Row | What it answers | May its text change? |
| --- | --- | --- |
| Article | What is the editor working on? | Yes |
| ArticlePublicationSnapshot | What complete content was approved at this publication? | No |
| ActiveArticlePublication | Which approved snapshot and URL are live now? | Its reference can change |

This is separate from M23's edit history. An edit-history row explains a prior editorial change. A publication snapshot is an approved public artifact. One can exist without the other having the same content or timestamp.

Trace these concrete values on paper:

1. Draft: title Refunds, slug refunds, body A, collection X. No active publication.
2. Publish: snapshot P1 contains those values; the active row selects P1.
3. Edit: title Refund requests, slug refunds-new, body B, collection Y. P1 stays unchanged.
4. Read public `/refunds`: get A in X. Public `/refunds-new` returns 404.
5. Publish: create P2 and switch the active row to P2 in one commit.
6. Now `/refunds-new` returns B in Y; `/refunds` returns 404.

`HasUnpublishedChanges` compares the editorial fields with the active snapshot. `Status=Published` alone cannot tell a public reader which body to use. That is why every public article lookup, search, collection count, suggestion, and related-article query must follow the active snapshot.

## Guided code reading

Start at `HelpCenterController.UpdateArticleCore`. Find the history snapshot, editorial assignments, and schedule invalidation. Notice that this path does not replace an active publication.

Next read `ArticlePublications.PublishAsync`. Identify validation before mutation: the collection must still exist, and another live article must not own the proposed public slug. Then find the new immutable snapshot and active-reference replacement. The helper stages database changes; its caller owns the transaction and save.

Follow `ArticlePublications.Public` into `PublicHelpCenterController`. Explain why querying the editorial Article table with a Published filter would leak private text. Repeat the trace for `ArticleLearningController.Related` and conversation suggestions. The ranker accepts temporary Article-shaped values made from snapshots; those objects are never attached to EF.

Finally examine deletion. The active row references a snapshot with a restrictive foreign key. Deleting the route before deleting the article can require two saves. Two saves are safe here because one outer transaction contains both: a failure rolls both back. “One save” and “one atomic command” are different concepts.

## Third explanation: scheduling is approval plus a future check

A schedule records the current article version, an intended UTC time, the scheduling Admin, and a durable status. The version says which draft was reviewed. If someone edits that draft, the schedule becomes NeedsReview. It must not silently publish the new text using approval for the old text.

The timer is only a wake-up mechanism. It does not remember the job. `PublicationScheduler.ProcessDueAsync` reads stored due schedules and handles at most 20 per pass. Each one gets a fresh context and a short write transaction. Inside that transaction the processor rechecks:

- Is this still Pending and due?
- Is this still the approved article version and a Draft?
- Is the scheduling teammate still an Admin in the same workspace?

Valid work uses the same publication service as the manual button and marks the schedule Completed in the same save. A conflicting public slug becomes NeedsReview. A database failure leaves the publication and schedule uncommitted. A later pass can try again.

The worker is disabled by default. `Workers:PublicationSchedules:Enabled=true` enables a 30-second scan. Publication happens on a successful due pass; the API does not promise exact-second timing. Tests call the processing service directly instead of sleeping for the timer.

## Failure timeline exercise

Draw two vertical lines labeled Database and Worker. Put a commit line across the database timeline. Place the following crash points above or below it:

| Crash point | Expected durable state | Next pass |
| --- | --- | --- |
| Before a due row is selected | Pending, private | Select it |
| After staging snapshot but before commit | Pending, private | Retry from durable state |
| After commit but before the worker logs success | Completed, public | Skip it |

Do not treat a log line as the transaction receipt. Inspect the schedule and active publication to learn what committed.

For two competing processors, both may initially discover the same due ID. The second must recheck after obtaining the write transaction. Once the first commits Completed, the second does nothing. The initial scan is a list of candidates, not ownership.

## Practice without memorizing

Run the S10/S17 tests and select one assertion before reading its implementation. Predict which rows should exist. Change body B to a distinctive phrase and trace why public search cannot find it before publication. Then undo your experiment.

Inject failure on the snapshot INSERT using the test factory's `FailCommand`. Before rerunning, write your expected old URL, new URL, number of snapshots, and schedule status. Compare actual rows with that prediction. A successful rollback proves more than a 500 response: it proves the old public state remains whole.

On another day, explain the same feature as a librarian: a working manuscript, a printed edition, and a catalog pointing readers to the approved edition. Then return to the real names. The analogy helps recall roles, but transaction boundaries and version checks still need exact code-level reasoning.

## Upgrade and handover

Existing Published articles need one initial snapshot and active route. Existing drafts need none. The upgrade must preserve body, title, slug, collection, and first-publication timestamp. Test a real old-schema database, migrate it, and compare public results before and after.

The older SQLite article-token migration initially gave existing rows one shared default value. Scheduling needs each article's initial token to distinguish that article, so the publication upgrade replaces only that legacy default with the article's unique ID. Already changed tokens remain intact. A dedicated old-schema test verifies that two legacy drafts no longer share an initial token. This is a useful reminder that “nonempty” and “distinct per resource” are different migration requirements.

After private edits exist, rolling back to an old public reader would expose them. A code rollback therefore needs a deliberate data and reader plan; simply swapping the binary is insufficient. Keep the scheduler disabled until its migration, normal publish path, and one disposable due job are verified.

Teach back: why does a schedule need both a time and a version? Why can changing a draft collection leave the old public collection nonempty? Why are immutable snapshots insufficient without one atomic active-reference update? Use the A/P1/B/P2 example to answer all three.
