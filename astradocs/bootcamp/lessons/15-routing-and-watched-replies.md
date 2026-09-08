# 15: One workflow across two channels

Stories: S08-S09. Check [progress](../progress.md) before treating these implementation examples as verified results.

## Start with a decision table

A support team wants enterprise customers to reach specialists, regardless of whether the conversation begins in chat or email. Administrators define ordered rules. The first matching usable rule assigns a team; no match retains the existing auto-assignment fallback.

| Contact and configuration | Decision |
| --- | --- |
| Enterprise email; rules A and B both match | Team from A |
| Same contact; A's team was removed | Skip A and consider B |
| Free customer; no rule matches; AutoAssign on | Existing teammate picker |
| Any customer; empty rule list | Existing fallback behavior |
| Existing conversation; rules are replaced | Its assignment remains unchanged |

These examples define precedence before implementation. “Use routing rules” is incomplete without saying whether rules override auto-assignment, what first means, and what happens when a referenced team is unavailable.

## Separate the active definition from historical versions

Read [InboxRouting.cs in Domain](../../../src/Harbor.Domain/Entities/InboxRouting.cs). The definition identifies the inbox's active version and carries an optimistic token. Each immutable version records its rule JSON, schema version, creator, and creation time.

PUT replaces the active definition using ExpectedVersion. Null means create only when no definition exists; an existing definition requires its current token. The comparison and replacement occur inside a writer transaction, so two administrators cannot both pass the same comparison and silently overwrite one another.

Rules carry stable IDs, names, target team IDs, optional priority/channel constraints, and a segment condition set. At most 20 rules are allowed, each with 1-10 conditions. The stored definition is also bounded in size. An empty list is valid and disables routing while retaining a versioned record of that configuration change.

An assignment event records the chosen routing version and rule IDs as historical values. Editing the active configuration later does not rewrite the explanation of an older assignment. This is the same distinction between current state and historical evidence that appeared in article revisions and webhook attempts.

## Follow a preview without causing an assignment

Read [InboxRoutingController.cs](../../../src/Harbor.Api/Controllers/InboxRoutingController.cs), then `EvaluateAsync` in [InboxRouting.cs in Infrastructure](../../../src/Harbor.Infrastructure/InboxRouting.cs).

The preview validates the owned inbox and contact, reads the active version, checks usable team IDs, and evaluates the ordered contact predicates in SQL. It returns a selected team/rule/version or no match, with skipped-rule reasons. It does not call AutoAssigner, move an inbox rotation pointer, create a conversation, or publish a webhook.

The evaluator checks candidates in order. Missing target teams and mismatched constraints are skipped. The existing SegmentCompiler supplies contact matching; the new feature does not invent a second interpretation of missing attributes or negative operators.

Explain this as a row of gates: a rule's target must exist, its priority/channel restrictions must match, and its contact predicate must match. Passing all gates selects that rule and stops. Skipping a gate leaves a reason and continues to the next rule.

## Why a new email contact must be saved inside a transaction

Chat starts with an existing contact. Inbound email can construct a brand-new contact whose attributes and email exist only in EF's change tracker. A SQL query cannot see an object that has never been inserted into the database.

Read the new routing portion of [ConversationStarter.cs](../../../src/Harbor.Api/Infrastructure/ConversationStarter.cs). When routing exists and the contact is Added, it saves that contact before SQL evaluation. The caller must already own an outer transaction. This intermediate SaveChanges makes the contact visible to SQL on that connection; it does not commit the complete operation.

After routing, the starter stages the conversation, opening message, assignment history, and outbox events. The caller commits those together. If final conversation creation fails, the earlier contact insertion rolls back too. Both keyed and unkeyed chat starts and inbound ingestion use the shared transaction helper around start orchestration.

Draw two boxes named SaveChanges inside a larger box named Transaction. Put contact insertion in the first and the conversation/outbox in the second. Only the larger box's commit makes either permanent. This picture is often easier to remember than ORM terminology.

## Team matches preserve the fallback's state

A matched rule calls AssignToTeam and creates a Routing assignment event. It does not call the teammate picker. The assignment webhook is staged for team assignments as well as teammate assignments.

No match follows the established AutoAssign rule. Leaving the fallback intact matters because availability, capacity, and rotation behavior were already defined elsewhere. Reusing the existing picker avoids a new routing endpoint that accidentally changes capacity semantics for unrelated inboxes.

Read [S08RoutingRulesTests.cs](../../../tests/Harbor.Tests/Integration/S08RoutingRulesTests.cs). The same decision table is exercised through chat and new-email starts. The test checks the rotation pointer as well as the chosen team; a correct-looking assigned team could otherwise conceal an unnecessary picker call.

## Watching is a personal relationship

S09 lets a teammate watch a conversation without changing its assignee. Watch/unwatch are idempotent personal operations. A conversation has at most 200 watchers, enforced by a count-and-insert writer transaction.

Read [ConversationWatching.cs](../../../src/Harbor.Domain/Entities/ConversationWatching.cs). A watcher is identified by conversation plus teammate. A notification has its own ID and a unique message/teammate pair. The first key prevents duplicate subscriptions; the second prevents duplicate consequences for one recipient and message.

There is no administrator bypass for another person's notification list. The listing and mark-read queries always constrain the authenticated owner. Marking one person's notification read leaves everybody else's row unchanged.

## Fan-out belongs to the message transaction

Read [ReplyNotifications.cs](../../../src/Harbor.Infrastructure/ReplyNotifications.cs) and its call from [MessageAppending.cs](../../../src/Harbor.Api/Infrastructure/MessageAppending.cs). It stages notifications only for Contact/Reply messages appended to existing threads. Opening messages, teammate replies, and notes do not produce them.

The helper reads the current watcher set and stages one metadata row per recipient. The message, conversation activity, SLA effects, outbox, and notifications are saved together. A notification pointing to a message that rolled back would describe an event that never became real, so it must share that transaction.

Recipients are the watcher set observed by the query. Someone who begins watching later is not promised a notification for an earlier reply. Unwatching affects future fan-out and preserves old notifications. No email or push delivery is part of this story.

Try the two-person example: A and B watch C; customer reply M creates N(A,M) and N(B,M). A marks theirs read. B still has one unread item. B unwatches. The next reply creates another notification for A only, while B can still read the old one.

## Read the failure evidence

Open [S09WatchedReplyTests.cs](../../../tests/Harbor.Tests/Integration/S09WatchedReplyTests.cs) for both-channel behavior and owner isolation. Then open [ContactWorkflowRollbackTests.cs](../../../tests/Harbor.Tests/Integration/ContactWorkflowRollbackTests.cs).

The routing failure test observes contact insertion and interrupts the later conversation insertion. It then checks that neither contact nor conversation/receipt survived. The notification failure test interrupts the child insertion and checks that the new message and activity version also rolled back. These tests locate failures inside the workflow instead of only testing invalid inputs before any write begins.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~S08|FullyQualifiedName~S09|FullyQualifiedName~ContactWorkflowRollbackTests|FullyQualifiedName~ChannelParityTests|FullyQualifiedName~MigrationDriftTests'
```

For tomorrow's teach-back, explain three separate concepts using one email reply: channel identity, shared lifecycle effects, and personal notification ownership. Then use one new email conversation to explain why SQL evaluation requires an inserted contact and why insertion does not necessarily mean commit.
