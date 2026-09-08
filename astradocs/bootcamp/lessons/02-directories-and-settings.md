# Lesson 02: search a directory, change a setting

Stories: US-06 through US-15. Read the [original exercises](../../16-junior-user-stories.md) and track evidence in [progress](../progress.md). The implementations are in place; verification status belongs in the tracker, not in an assumption that this page proves they work.

## Start with a support agent's day

You need to find the billing team. There are many teams, and opening every team wastes time. A text filter narrows the list. Next you need an available agent; a role filter and an availability filter answer different questions. Finally, an administrator decides whether an inbox should automatically assign newly arriving conversations.

These look like small changes. They teach three separate responsibilities: selecting data, describing data safely, and changing future behavior.

## Read the code in this order

1. Open [Requests.cs](../../../src/Harbor.Api/Contracts/Requests.cs). Find `InboxFilterRequest`, `TeammateFilterRequest`, and the two inbox update requests. These describe what an HTTP caller may ask for.
2. Open [TeammatesController.cs](../../../src/Harbor.Api/Controllers/TeammatesController.cs). Follow `List`: workspace boundary, optional conditions, order, response mapping, page. Say each stage aloud.
3. Open [InboxesController.cs](../../../src/Harbor.Api/Controllers/InboxesController.cs). Compare `List` with `SetAutoAssign`. Reading uses `AsNoTracking`; changing a row uses a tracked entity and `SaveChangesAsync`.
4. Open [Responses.cs](../../../src/Harbor.Api/Contracts/Responses.cs). Compare `TeammateResponse` with `TeammateCreatedResponse`. A key is returned on creation; directory search must use the safe response.
5. Read [DirectoryLearningTests.cs](../../../tests/Harbor.Tests/Integration/DirectoryLearningTests.cs). For each test, separate arrangement, HTTP action, and observable result.

## The same filter explained three ways

Plain language: find people in this workspace whose name or email contains the search text, then keep only people with the selected availability and role.

Set language: start with the workspace's people. Text, availability, and role each remove nonmatching people. None of those steps adds anyone from another workspace.

Predicate language:

```text
workspace matches
AND (name contains q OR email contains q)
AND (availability omitted OR availability matches)
AND (role omitted OR role matches)
```

The parentheses matter. If email matching sits outside the workspace condition, someone else's teammate can leak into your results. Search tests should arrange a matching foreign row, not merely two unrelated rows.

## Work through this example

| Person | Role | Availability | Email |
| --- | --- | --- | --- |
| Ada | Agent | Away | ada@billing.example |
| Bea | Agent | Available | bea@billing.example |
| Boot Admin | Admin | Available | admin@example.test |

Predict before running:

1. `q=billing` returns Ada and Bea.
2. Add `availability=Away`; now only Ada remains.
3. Add `role=Admin`; now the result is empty.
4. Remove all filters; all three people return.

Empty results are a successful query. A malformed role is an invalid request. Those are different situations and should have different HTTP outcomes.

## Optional does not mean unvalidated

`bool?` has three states. `null` means no restriction, `true` selects enabled rows, and `false` selects disabled rows. A malformed value such as `banana` cannot be represented as a boolean and returns 400 through model binding.

An enum is trickier: an integer can fit into the underlying storage even when the application has no matching enum member. `EnumDataType` rejects undefined numeric values on the new teammate filters. Read the invalid-enum tests and explain why testing only a misspelled word would miss this case.

The write request uses a required nullable boolean. A caller must send an explicit choice. `{}` and `{ "autoAssign": null }` return 400; `{ "autoAssign": false }` is valid. If the property were a plain boolean without presence validation, an omitted setting could silently become false.

## Configuration changes are not historical rewrites

Imagine three conversations:

```text
autoAssign false -> start A -> A unassigned
autoAssign true  -> start B -> B assigned if an eligible teammate exists
autoAssign false -> start C -> C unassigned
```

After all three steps, A remains unassigned and B keeps its assignee. The setting determines what the conversation-start workflow does next. Updating the setting does not invoke the assignment workflow on old conversations.

The test makes the bootstrap administrator Away and creates one available agent. That fixture removes ambiguity: B should go to that one agent. Without controlling eligibility, the test could accidentally rely on whichever eligible person the assignment algorithm chooses first.

Availability is only one input to eligibility. An Available agent can still be at capacity. The directory's availability filter reports the stored availability; it does not pretend to calculate assignment eligibility.

## Small write, narrow permissions

`Rename` changes only `Name`, trims the outside, and preserves inner spacing. `SetAutoAssign` changes only its flag. Both require Admin and look up the inbox by workspace **and** ID. A scoped lookup returning nothing produces 404. An authenticated agent lacks the required role and gets 403 before making the change.

This is a useful review habit: list the fields the feature is allowed to change, then inspect every assignment in the action. For rename, an assignment to the email address, SLA duration, or round-robin pointer would need a separate explanation.

## Search, exact match, and pagination

The canned-reply `shortcut` parameter is an exact, case-insensitive match after trimming. `refund` must not also select `refund-late`. The older `q` parameter is a substring search across shortcut, title, and body. When both are present, both conditions must hold.

The database filters first, counts that filtered set, orders it, and returns one page. Page size changes how much you see, not how many rows match. Team search also retains the roster in each returned `TeamResponse`; adding a filter must not silently change the response's meaning.

## Practice without copy-pasting

1. Explain why `role=Agent&availability=Away` can legitimately return people who cannot take new work.
2. Predict the total count for a two-row match with `pageSize=1&page=2`.
3. Show where the API prevents a key hash from appearing in teammate search.
4. Sketch a failing test for an inbox whose email matches the text but whose name does not.
5. Find the line that preserves old assignments when auto-assignment is disabled. There is no special preservation loop: the update action never edits those conversations.
6. Temporarily change shortcut equality to `Contains` on a practice branch. Run the shortcut test, read the failure, and undo that experiment.

Answers: the role and stored availability say nothing about remaining capacity; total stays two; the safe response mapping selects the public fields; arrange an email-enabled inbox with a different name and assert its ID; the narrow setting update leaves history untouched; the exact-shortcut test distinguishes `refund` from `refund-late`.

## Teach it back

In two minutes, explain a read endpoint and a write endpoint without reading the source. Include workspace scope, validation, response safety, and one test that would catch a plausible mistake. Then open the files and correct anything you remembered incorrectly. Repeat tomorrow with the team and canned-reply endpoints.
