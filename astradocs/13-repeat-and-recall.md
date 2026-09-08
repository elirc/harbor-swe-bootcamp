# 13: Meet the same ideas again

**Remember today:** recognizing an explanation and explaining it from memory
are different things you can practice.

Choose one group per session. Read the three explanations, cover them, answer
the question, then check. Revisit a group next session and after a few other
sessions. This is a suggested routine, not a deadline for understanding.

## Group 1: conversation versus message

**Story:** Maya's invoice thread is a conversation. Her question and Ada's reply
are messages inside it.

**Container:** the conversation is a folder for the thread; a message is one
entry. This is an analogy, not literal files on disk.

**Code:** [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs) has a
`Messages` collection containing `Message` entities.

Recall question: a thread has a question, two replies, and a note. How many
conversations and messages are described?

<details>
<summary>Answer</summary>

One conversation and four messages. A note is also a message, distinguished by
its kind.

</details>

## Group 2: responsibility versus folder name

**Story:** the API receives the operation, the domain expresses behavior, and
infrastructure supplies database/integration capabilities.

**Questions:** what was requested; what behavior is valid; how does data persist?

**Code:** `ChangeState` calls `Conversation.Snooze`, then the controller saves
through `HarborDbContext`. Controllers can also query the database directly.

Recall question: where would you first inspect “snoozing at the current time
should fail”: the route attribute, the `Snooze` method, or a migration?

<details>
<summary>Answer</summary>

Start with `Conversation.Snooze`, which contains the time rule. Then inspect
its unit test and how the API translates a failure. A route maps an operation;
a migration changes database schema.

</details>

## Group 3: query versus loaded rows

**Instructions:** an `IQueryable` describes selection. Executing it produces results.

**Paper exercise:** “keep Acme's open cards” describes a rule; collecting the
matching cards gives you a result.

**Code:** `Where` composes membership; `CountAsync` executes a count;
`ToListAsync` materializes rows.

Recall question: should a large-data filter usually be applied before or after
loading every conversation into a list?

<details>
<summary>Answer</summary>

Compose it before materialization so the database can filter rows. Loading
everything first can produce the same small-demo answer while doing much more
work. Whether a particular expression translates must still be checked.

</details>

## Group 4: preparation versus save

**Story:** the starter prepares the conversation and related work; its caller saves.

**Milestones:** object created, entity tracked for insertion, changes committed.

**Code:** `new Conversation`, `db.Conversations.Add`, and `SaveChangesAsync`
mark different points in creation.

Recall question: which establishes successful persistence in this path?

<details>
<summary>Answer</summary>

Successful completion of the save establishes the related database commit in
this path. Constructing an object or adding it to the context is not committing
it. Returning JSON is also not a persistence operation.

</details>

## Group 5: page versus matching population

**Story:** Ada sees two conversations at a time out of five matching conversations.

**Numbers:** page length = 2, matching total = 5.

**Code:** `CountAsync` runs before `Skip`/`Take`; overview counts the whole
filtered query and ignores page selection.

Recall question: can an empty page have a nonzero matching total?

<details>
<summary>Answer</summary>

Yes. A page past the end is empty even when the filter matches earlier rows.
For example, page 100 can be empty while the matching total is five.

</details>

## Group 6: identity versus permission

**Story:** Ada's key identifies her; it does not let her read another company's work.

**Questions:** who is calling; may they perform this operation on this data?

**Code:** the key handler builds identity, while authorization and tenant-scoped
lookups enforce access.

Recall question: why include workspace scope when looking up an existing conversation ID?

<details>
<summary>Answer</summary>

Existence alone does not establish access. The caller must not read another
tenant's conversation by knowing its ID. The scoped lookup hides foreign records
like missing ones.

</details>

## Group 7: current state versus history

**Story:** a thread can be open now even though it was resolved earlier.

**Labels:** `State` describes now; `FirstResolvedAt` preserves the first resolution.

**Code:** reopening clears `ClosedAt`, while the first-resolution milestone remains.

Recall question: why can closed work appear with `slaBreached=true`?

<details>
<summary>Answer</summary>

It may have been resolved after its deadline. Being closed now does not undo a
missed target in its history.

</details>

## Group 8: queued versus sent

**Story:** a delivery task was saved, but nobody has yet dispatched it.

**Two systems:** the database stores pending work; an HTTP request reaches an
external receiver later.

**Code:** `Webhooks.PublishAsync` queues work; `WebhookDispatcher.DispatchAsync`
attempts delivery. These are different methods and boundaries.

Recall question: does a pending outbox row prove the subscriber received a message?

<details>
<summary>Answer</summary>

No. Dispatch must happen, the receiver may fail, and attempts may repeat. The
current host does not automatically run a background dispatch loop.

</details>

## Quick checks with different wording

Answer just three at a time:

1. Does a GUID act like a password?
2. If two timestamps tie, what gives conversation paging a unique order?
3. Does that order freeze the dataset between requests?
4. Does a note satisfy first response?
5. Can a note update activity while leaving state unchanged?
6. If a date window ends at noon, does a row created exactly at noon belong in it?
7. Does `await` mean immediately return success to the HTTP caller?
8. Is the in-memory SQLite test database your Development `harbor.db` file?
9. Does a health response establish that the outbox is being drained?
10. Can two incorrect implementations agree with each other?
11. Does adding a query-string filter always need a migration?
12. Where do you begin when the repository feels too large?

<details>
<summary>Answers, in the same order</summary>

1. No: an identifier is not a credential or permission.
2. ID ascending breaks ties for the supported conversation sort modes.
3. No: concurrent changes can still shift offset pages.
4. No.
5. Yes.
6. No: `createdBefore` is exclusive.
7. No: the code awaits the operation before continuing that path.
8. No: the test factory uses its own in-memory connection.
9. No: health and worker progress are different observations.
10. Yes: compare them with independent expected results too.
11. No: filtering an existing property can need no schema change.
12. Choose one user operation and follow its route, action, and next call.

</details>

## A note for next time

```text
Idea I revisited:
My explanation without looking:
The part I mixed up:
A source method that settles it:
A different example I can now explain:
```

If an answer is hard, return to its guide or linked code. You have identified
the next small thing to practice. Next: [short sessions](14-onboarding-sessions.md).
