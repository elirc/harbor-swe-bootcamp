# Lesson 04: personal work and stale browser tabs

Stories: M01-M04. These are the first features that add private persisted data and
client-visible version checks. Their status is recorded in [progress](../progress.md).
The current full Release suite passed 857 tests. Use the linked tests to inspect
which stale-write, ownership, and upgrade cases support each claim.

## Four features that solve different problems

| Feature | What is saved | Who owns it | Effect on a conversation |
| --- | --- | --- | --- |
| Saved queue | A named filter recipe | One teammate | Reads current matching conversations |
| Bookmark | A teammate-to-conversation relationship | One teammate | None |
| Reply draft | Unsent text and its version | One teammate | None until a separate send action |
| Subject editor | The conversation's subject | Shared conversation | Updates metadata when text changes |

Before opening the code, tell a short story about an agent using all four:
open a saved billing queue, bookmark a difficult thread, draft an answer, then
correct that thread's subject. Notice which actions should be visible to another
agent. The subject is shared. The other three are personal.

## Ownership is a conjunction

Earlier lessons scoped reads to a workspace. Private resources add a condition:

```text
resource ID matches
AND workspace matches the authenticated caller
AND owner matches the authenticated teammate
```

A workspace administrator does not automatically gain access to a colleague's
private draft under these contracts. The role authorizes access to the feature;
the ownership predicate selects the particular row the caller may use.

Draw two people inside one company boundary. Put a draft under each person and
draw both drafts pointing to the same conversation. Deleting one person's draft
must not delete the other. This is why the database relationship includes owner
and conversation, rather than using only the conversation ID.

Read [PersonalWork.cs](../../../src/Harbor.Domain/Entities/PersonalWork.cs) and the
new entity configurations in [HarborDbContext.cs](../../../src/Harbor.Infrastructure/HarborDbContext.cs).
Find the unique `(ConversationId, OwnerTeammateId)` index on drafts and the
composite bookmark key. Explain the allowed and forbidden duplicates.

## A saved queue is a recipe

At 09:00 your saved recipe is `state=Open`. Conversation A and B match. At 10:00
someone closes A. Running the same recipe now returns only B. The saved queue did
not change; the data it selects changed.

Saving `[A, B]` would preserve yesterday's membership, which is a different
feature. Saving an EF `IQueryable` would try to preserve executable query state,
which is not a durable data contract. Instead, `SavedQueueFilter` explicitly lists
the supported criteria, and `SchemaVersion=1` says how to interpret the JSON.

Read [PersonalWorkContracts.cs](../../../src/Harbor.Api/Contracts/PersonalWorkContracts.cs).
Locate the mapping from a saved recipe to `ConversationFilterRequest`. Every
criterion should survive that mapping. Paging is supplied by each execution and
has no property in the saved recipe.

Then read [SavedQueuesController.cs](../../../src/Harbor.Api/Controllers/SavedQueuesController.cs).
Follow create, reload, validate, and execute. An unsupported stored schema or
missing segment produces 422. Silently ignoring a missing segment could broaden
a small queue to the whole workspace, which would violate the saved intent.

Practice: save an Open queue, close one member through the normal endpoint, and
predict its next result. Then change the saved recipe to a nonexistent segment.
Explain why the second outcome is an error, not an empty queue or an unrestricted
queue.

## A bookmark is an idempotent relationship

Here, idempotent means repeating the same request has the same intended final
state. PUT twice still leaves one bookmark. DELETE twice still leaves no bookmark,
provided the authorized parent conversation exists.

An initial existence check is useful but is not a concurrency guarantee:

```text
Request A: bookmark absent
Request B: bookmark absent
Request A: insert
Request B: insert
```

The composite database key prevents two identical bookmarks. The controller
handles that particular duplicate by detaching the failed inserted entity and
checking that the intended bookmark now exists. Other database errors still
propagate. Returning success for every exception would hide failed writes.

No conversation assignment or timestamp is edited. Read the bookmark test in
[PersonalWorkLearningTests.cs](../../../tests/Harbor.Tests/Integration/PersonalWorkLearningTests.cs)
and find assertions that demonstrate this lack of side effects.

## Two tabs, one draft

Work through the sequence slowly:

| Step | Tab A | Tab B | Stored draft |
| --- | --- | --- | --- |
| 1 | Reads V1 | Reads V1 | V1: Checking now |
| 2 | Saves with expected V1 | Still editing | V2: Found the answer |
| 3 | Finished | Saves with expected V1 | V2 remains; B receives 409 |

The token is an opaque identifier, not a number the client increments. The client
returns the value it last read. The server checks whether that value still
describes the current row.

Null `ExpectedVersion` on PUT means create only. It does not mean overwrite any
existing draft. An existing row requires the exact token. DELETE also requires
the current token so a stale tab cannot delete text another tab just saved.

After 409, the client should reload and compare its local text with the saved
text. It should not silently fetch a new token and overwrite anyway; that would
remove the protection the user needed.

## There are two checks, for two different gaps

The controller compares the expected token to the loaded row. This detects a
client that was already stale before the request started.

EF uses the original concurrency token in the database UPDATE or DELETE
condition. This detects another writer changing the row after the controller
loaded it. The context generates the new token when saving a modified entity.
The existing concurrency exception handler returns 409 for a lost race.

Conceptually:

```sql
UPDATE ReplyDrafts
SET Body = @newBody, Version = @newVersion
WHERE Id = @id AND Version = @originalVersion;
```

If zero rows match, the original version is no longer current. A C# `if` alone
cannot protect the time between reading and writing. A sequential two-tab HTTP
test demonstrates stale-client behavior; overlapping database-context tests are
needed to demonstrate the database race behavior. Do not claim the latter from
the former.

## Drafts are not messages

Saving a draft must not end the first-response wait, update conversation
activity, create an email message ID, or publish an outbox delivery. Keeping drafts
in their own table makes this separation explicit. Read the save action and list
which tracked rows it modifies: the draft row only.

The tests compare the conversation's timestamps and message count before and
after draft changes. Later email-specific and overlapping-write checks extend
that evidence; the tracker should distinguish what has actually run.

## Subject editing uses the conversation's version

The subject editor exposes a small response: ID, subject, version. It uses the
existing conversation token. A priority change or new message can therefore make
the editor's version stale even if nobody edited the subject. This is the chosen
whole-conversation precondition, and clients must understand it.

Read `ChangeSubject` in [Conversation.cs](../../../src/Harbor.Domain/Entities/Conversation.cs).
It trims outer whitespace, turns blank text into null, preserves inner spaces,
and changes `UpdatedAt` only when normalized text actually changes. It does not
change `LastMessageAt`: metadata editing is not a new message.

Read [ConversationEditingController.cs](../../../src/Harbor.Api/Controllers/ConversationEditingController.cs)
and [SubjectEditingLearningTests.cs](../../../tests/Harbor.Tests/Integration/SubjectEditingLearningTests.cs).
Find the no-op, clear, stale-version, and foreign-workspace cases. Explain why a
valid no-op may return the same token.

## Migrations are part of the feature

The new C# entity is only one description of the system. Existing installations
also need database tables, indexes, and foreign keys. `EnsureCreated` in a test
fixture creates the newest model directly and can hide a missing migration.

The schema checks must cover the migration snapshot and a database upgraded
through migrations. Review the generated migration for unexpected table drops
or changes to unrelated columns. Add explicit reload and uniqueness checks for
these private resources. A green API test against a fresh model is not an upgrade
test.

## Repeat it in your own words

1. Describe a saved queue as a shopping-list rule: "all items still needed."
   What happens when an item is purchased? Map that back to closing a conversation.
2. Describe a bookmark as your own sticky note on a shared file. Why does removing
   your note leave a colleague's note intact?
3. Describe a version token as the identity of the edition you edited. Why should
   the server reject an edit based on an older edition?
4. Return to the actual classes and explain the same rules without analogies.

Your completion exercise is a short review note: name one ownership predicate,
one database constraint, one side effect deliberately absent from a draft save,
and one concurrency claim the tests have not yet proved.
