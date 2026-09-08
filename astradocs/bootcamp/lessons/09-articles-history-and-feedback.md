# 09: An article has identity, history, and an audience

Stories: M22-M25. See the [tracker](../progress.md) for verification status. These features introduce editorial history; the later S17 story separately addresses isolation between private edits and public content.

## Start with a concrete editing session

An administrator creates an article with body A, edits it to body B, publishes it, and unpublishes it. The current article ends as a draft containing B. Its revisions contain the states immediately before the three changes: draft A before the edit, draft B before publishing, and published B before unpublishing. Creation does not create a revision. Repeating a publication operation that changes nothing does not create another revision.

Draw the article as a whiteboard and revisions as photographs. The photograph is taken before erasing the whiteboard. A clone is a different whiteboard initialized with copied content. Feedback is a person's current opinion about the original whiteboard's identity. These objects are related, but they have different lifetimes and keys.

Read [ArticleEditorial.cs](../../../src/Harbor.Domain/Entities/ArticleEditorial.cs). Revisions record their own ID, the article ID, the actor, the reason, the source version, and a copy of editorial fields. Feedback uses article ID plus teammate ID as its identity. Those keys express two product rules: many historical revisions are allowed, but only one current vote per person and article is allowed.

## A clone is independent

Trace `Clone` in [ArticleLearningController.cs](../../../src/Harbor.Api/Controllers/ArticleLearningController.cs).

1. The authenticated administrator identifies an owned source article.
2. Validate that the destination collection belongs to the same workspace.
3. Normalize the requested slug using the existing slug rules.
4. Construct a new article with a fresh ID, copied body, requested title/collection/slug, and Draft status.
5. Save it and return its normal article URL.

Copying a published article does not publish the clone. The clone does not inherit revisions or votes. Editing the clone's body cannot change the source's body because these are separate persisted article rows. A slug collision returns 409; a related destination outside the workspace cannot be used to move content across tenants.

Try explaining it as a database operation: clone inserts a row with selected scalar values. It does not reuse the source primary key, copy every navigation property, or transfer ownership of existing history. This explanation is useful when an ORM makes an object graph look easy to copy accidentally.

## Why snapshot and update belong to one commit

Read [ArticleHistory.cs](../../../src/Harbor.Infrastructure/ArticleHistory.cs), then the edit/publish/unpublish actions in [HelpCenterController.cs](../../../src/Harbor.Api/Controllers/HelpCenterController.cs).

`Snapshot` stages a revision in the change tracker. It does not save. The controller validates first, detects no-ops, stages the snapshot before mutating the article, then saves both together. If an edit is invalid, it should not create history. If the update fails, its snapshot should not remain as evidence of an edit that never happened.

The article now carries an optimistic concurrency token. Two database contexts can load version V. The first saves a new article state and receives a new token. The second tries to update a row that no longer has V; EF reports a concurrency failure. The database transaction also rolls back that second context's staged revision.

This token guards overlapping server-side read/write operations. M23 does not by itself make every existing edit endpoint require the client's version from an earlier browser tab. Distinguish those two guarantees when reviewing a feature: a database concurrency token and an explicit HTTP precondition are related but not interchangeable.

Open [EditorialPersistenceTests.cs](../../../tests/Harbor.Tests/Unit/EditorialPersistenceTests.cs). The test first builds the old schema and inserts an old-format article with explicit column names. After upgrading, it checks that the old body survives and a token exists. Two independent database connections then stage overlapping edits. Only the winning edit's before-state should remain in history.

Why not use only `EnsureCreated`? That creates today's schema directly. It cannot show whether yesterday's database upgrades correctly. Why separate connections? Two objects in one context would share change tracking and would not reproduce two independent readers.

## Read history economically

The revision list projects metadata without downloading full bodies. Opening a particular revision returns its content. Both reads first establish the parent article's workspace ownership and then restrict the revision to that article. A revision ID alone is not permission to read it.

Think of the list as a table of contents and the detail endpoint as a chapter. Returning every chapter just to draw the table of contents would waste memory and network bandwidth. This separation also makes it easier to preserve pagination behavior as article bodies grow.

## Related articles are bounded suggestions

Related content begins with a published public source article. It extracts keywords from that source, loads at most 200 other published articles in the same collection and workspace, and ranks that bounded candidate set. The result limit is 1-10. Ties end with article ID so repeated requests have a deterministic ordering when other ranking values tie.

The candidate cap and result limit solve different problems. A result limit of five does not stop a query from loading 100,000 candidates. The cap bounds work before ranking; the limit bounds the returned answer after ranking. This means an older excellent match outside the 200 candidates can be omitted. That is a deliberate bounded-search tradeoff, not a promise of global relevance.

When reading [ArticleSuggestions.cs](../../../src/Harbor.Domain/ArticleSuggestions.cs), separate candidate eligibility from ranking score. A perfect textual match in another workspace, a draft, or the source article itself must never become a candidate in this endpoint.

## Feedback means replace your opinion

An authenticated teammate can read, put, or delete their own helpfulness vote. PUT means the person's current answer becomes the requested boolean. Sending the same answer twice must not add two votes. Changing true to false moves one person between counters. Deleting an already absent vote is harmless after checking the parent article.

The database composite key enforces uniqueness even when two create requests overlap. The caller's teammate ID comes from authentication, not a body field. Administrators can read aggregate counts; the personal endpoint does not expose everybody's votes.

Explain the aggregate with three people: Ada says true, Sam says true, Jo says false. The counts are two helpful and one unhelpful. Ada changes to false: one helpful and two unhelpful, still three total. Editing the article does not erase these opinions in this story. A future requirement for votes per publication would require a different key and product definition.

## Practice with evidence

Read [ArticleLearningTests.cs](../../../tests/Harbor.Tests/Integration/ArticleLearningTests.cs) in this order: clone/history workflow, public related-content eligibility, feedback ownership and replacement, then the candidate-cap example.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~ArticleLearningTests|FullyQualifiedName~EditorialPersistenceTests|FullyQualifiedName~MigrationDriftTests'
```

Before running, predict the three revision bodies and statuses in the editing session above. Then find the assertions proving that a rejected duplicate slug does not leave an extra revision. Finally, draw the two-context stale-write schedule and mark the single successful commit.

For a follow-up exercise, design a restore-revision operation on paper. It should copy an old body into the current article, preserve the historical row, create a before-restore snapshot, validate today's slug/collection rules, and require a current version. Explain why simply deleting all newer revisions would lose useful evidence. Do not implement this extra feature as part of the existing story unless you deliberately choose it as practice.

Tomorrow's teach-back: explain clone versus history, history versus publication isolation, and caller ownership versus an arbitrary teammate ID. Use one request and one failure case for each pair.
