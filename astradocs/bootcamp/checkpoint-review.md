# Review a checkpoint like an engineer

Use this page after a lesson group. The goal is to explain an observed result and its limits, not to memorize the number of green tests. Start with [progress](progress.md) and [the journal](journal.md), then choose a checkpoint below.

## Foundation checkpoint: filters and pages

Use lessons 01-03. Draw six contacts: two with email, two with null email, one with spaces, one with tabs. Predict `hasEmail=false`, then combine it with a name search. Add one contact in another workspace. Explain why it never contributes to the results or pagination total.

Find the query's membership predicates, ordering, count, and page selection in source. Change the requested page to two. State the exact existing pagination header names from Paging.cs rather than guessing. The journal records why an assertion about X-PageSize was corrected to X-Page-Size.

You are ready to move on when you can describe three distinct outcomes: invalid query shape gives 400, a valid filter matching nothing gives an empty array, and a foreign resource-by-ID gives 404. Explain which layer owns each decision.

## Feature checkpoint: one save can cover several consequences

Use lessons 04-07. Follow a priority change and list every staged effect. Follow a private draft save and list what it must not change. These are different product operations even though both write to SQLite.

Arrange two tabs with the same version. Let one save, then try the other. Draw the original token and the token after the first save. Point to the expected-version check and the database concurrency token separately. Explain why server-side optimistic concurrency alone does not always protect a client editing data read an hour ago.

For the combined timeline, put a message, assignment, and SLA breach at the same timestamp. Explain the secondary sort keys and why the source kind is part of the identity. The page must be selected across the combined population, not separately from each source.

You are ready when you can name the single commit for each write and show which read endpoints must remain free of assignment, message, and outbox effects.

## Midlevel checkpoint: definitions before calculations

Use lessons 08-10. Explain why a resolution on Thursday can appear in a report filtered to conversations created Monday. Then explain why one teammate in two teams does not automatically double a team's workload.

For article history, write the three snapshots produced by edit, publish, and unpublish. Describe why an invalid edit must not leave a revision. For feedback, replace one person's true vote with false and calculate the totals without adding another person.

Render a template where the contact name itself contains double braces. Explain why the substituted value remains literal. Trace a sent template through the ordinary message workflow and identify its SLA and webhook consequences.

Finally, redrive a failed webhook on paper. Name which identifiers and payload fields remain unchanged and which transport row gets a new identity. Repeating the redrive command should find the existing child, not reset the failed original.

You are ready when your examples distinguish population, identity, state, history, and representation using this codebase's actual records.

## Senior checkpoint: fail between the boxes

Use lessons 11-13. Draw `validate → decide → stage → save → commit → respond` for ingress. Place an interruption after SaveChanges but before the outer commit. Then place it after commit but before the response. Explain why the retry behavior differs.

Draw `claim → commit → HTTP → conditional completion` for webhook dispatch. At each gap, write what the database knows and what the receiver may have done. Explain why Unknown is useful evidence and why it consumes an attempt slot.

For credentials, draw the legacy teammate hash column and the new credential table. Explain how the migration copies trust without recovering raw keys. Then revoke the initial key and show why a fallback to the old column would break revocation.

Read a separate-connection test and locate its gate or controlled stale-reader schedule. An ordinary sequential endpoint test can verify a response contract. It cannot by itself prove every concurrent interleaving. Say exactly which interleaving the test constructs.

## A short review note you can write yourself

Fill this in using one real test and one real implementation file:

```text
User problem:
Example input and expected result:
Invariant that must survive a failure:
Files and the responsibility of each:
Commit boundary:
One rejected input:
One competing-request schedule:
Command run and tests actually discovered:
Observed result:
What this result does not establish:
Question I can now answer without looking:
Question I will revisit tomorrow:
```

Do not write “all tests pass” when you ran only a filter. Do not write “feature broken” when the runtime refused to load the test assembly before reaching its assertions. Do not write “migration verified” from an EnsureCreated-only fixture. These distinctions are demonstrated by actual events in the journal.

Repeat a checkpoint in a different form the next day: first a table, then a spoken request trace, then a failure diagram. If one explanation is hard to follow, use the concrete fixture again. Understanding the same behavior through several representations is the purpose of this bootcamp.
