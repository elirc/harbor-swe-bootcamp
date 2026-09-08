# Lesson 06: customer context and explanations that match the code

Stories: M07 and M11-M16. These implementations and tests are being assembled into
the next verified batch; consult [progress](../progress.md) for actual status.

## Start with the person using the feature

An agent notices that a thread belongs in another inbox. They move it, correct a
few customer attributes, inspect possible duplicate contacts, and ask why the
customer belongs to a segment. Each action needs a precise promise about what
changes and what stays part of the same record.

| Operation | The identity preserved | What changes |
| --- | --- | --- |
| Transfer a chat thread | Conversation and its history | Inbox, metadata time, applicable SLA targets |
| Patch contact attributes | Contact and omitted map entries | Only explicitly supplied changes |
| Inspect duplicate emails | Every separate contact | Nothing |
| Summarize support history | Conversations and messages | Nothing |
| Preview rules | All stored records | Nothing |
| Clone a segment | Original segment | A new independent segment is created |
| Explain membership | Segment and contact | Nothing |

## Moving a thread is not starting another one

Read [TransfersController.cs](../../../src/Harbor.Api/Controllers/TransfersController.cs)
and `MoveToInbox` in [Conversation.cs](../../../src/Harbor.Domain/Entities/Conversation.cs).
The operation changes the inbox on the existing conversation. It does not call
`ConversationStarter`, copy messages, reassign the owner, or pretend the customer
just contacted support again.

For a thread created at 10:00, moving at 11:00 to an inbox with a 30-minute target
makes the due time 10:30. The clock is still anchored to creation. If no response
has arrived, the new target is already breached. The move, its history row, and
newly detected breaches save together.

Draw the same conversation box before and after, with its ID unchanged. Move only
the arrow pointing to the inbox. Keep the arrows to contact, messages, tags, and
assignee in place. This picture is a useful review checklist for accidental field
changes.

Moving to the current inbox returns without another transfer-history row. Email
threads are rejected in this slice because changing their inbox also raises sender
and threading questions that this contract has not answered. A recorded transfer
is available through its own history endpoint; the three-source timeline from
Lesson 05 does not silently claim to include it.

## Patch and replacement answer different questions

Suppose the current map is:

```json
{ "plan": "pro", "region": "eu", "language": "en" }
```

The requested changes are:

```json
{ "plan": "enterprise", "region": null, "tier": "gold" }
```

The result is:

```json
{ "plan": "enterprise", "language": "en", "tier": "gold" }
```

`language` remains because it was omitted. `region` disappears because it was
explicitly null. `plan` changes and `tier` is added because their values are strings.
Saving the changes dictionary as the entire map would accidentally discard
`language` and turn the patch into replacement.

Read [ContactAttributePatch.cs](../../../src/Harbor.Infrastructure/ContactAttributePatch.cs).
It validates all incoming keys before changing the copied map. Trimming keys can
make `plan` and ` plan ` collide, so duplicates are checked after normalization.
Key case and value whitespace are preserved. Empty changes return the original
serialized value as a no-op.

Bounds are part of the contract: at most 50 changes, normalized keys up to 100
characters, values up to 2,000 characters, and final JSON up to 10,000 characters.
A collection of individually valid values can still exceed the final-document
limit. Test both levels.

## A patch still needs a concurrency story

The contact now carries a version token. GET attributes exposes the current map
and token; PATCH requires that token. As with drafts, a C# comparison detects an
already-stale client and EF's original-token predicate protects a later database
race. A legacy full contact PUT remains replacement and does not acquire a client
precondition merely because the entity gained a token.

The upgrade gives existing contacts a nonempty initial token. Different existing
rows may begin with the same token because the database update is also scoped by
row ID. A token is not a globally unique permission or secret; it identifies the
version expected for that particular row. New contacts initialize their own tokens.

Read the model configuration and the generated migration together. Explain how a
database containing old contacts gets the new column, then explain why a test that
uses only `EnsureCreated` would miss a broken backfill.

## Possible duplicates are a read model

The duplicate-email endpoint groups nonblank trimmed/lowercase addresses in SQL,
keeps groups with at least two contacts, and pages groups by count descending then
email. Its total is the number of duplicate groups, not the number of contacts.

The members endpoint answers a second question: which contacts currently belong
to this duplicate group? It uses the same normalization, counts members, and
returns an empty paged set if fewer than two remain. A read transaction keeps
that decision and returned page consistent within the response.

Example: three B-address contacts and two A-address contacts produce two groups.
The B members count is three. A matching foreign-workspace contact changes neither
count. Deleting one of the two A contacts makes the A members endpoint empty.

This matching rule is a practical investigation aid, not a universal email
identity algorithm. It performs no merge. Read the separate contact histories
before deciding whether two records represent the same person; the later reviewed
merge story adds that separate operation.

## Aggregate at the level of the question

Three conversations with six messages are still three conversations. Joining
messages onto conversations and counting joined rows can inflate the conversation
total. The support summary aggregates conversation states separately from message
kinds, then selects the latest conversation by message activity and ID.

Read [ContactInsightsController.cs](../../../src/Harbor.Api/Controllers/ContactInsightsController.cs).
Point to the scoped contact check, conversation aggregate, message aggregate, and
latest-row query. Explain why missing contact is 404 while an existing contact
with no history receives zero counts and null dates/ID.

Closed is current state, not "ever resolved." A reopened thread counts as Open
even though its first-resolution milestone remains stored. Notes count as notes
and may become the latest activity without ending the first-response wait.

Draw three parent boxes with five reply circles and one note circle underneath.
Count boxes, reply circles, and note circles separately. Then map each count to
its DTO property.

## Preview is a structured read carried in a POST body

A rule set is more structured than a simple query-string filter. The preview POST
uses a body to carry it, but creates no saved segment. It validates shape first,
uses the existing compiler for meaning, counts every match, and takes a bounded
sample in name/ID order.

Shape failures include missing rules, null conditions, an unknown Match value, or
an out-of-range sample limit; they return 400. Unsupported field/operator/value
combinations reach compiler validation and return 422. Keeping these checks
separate prevents malformed nested input from becoming a null-reference 500.

With two matching contacts and sample limit one, the result is total two with one
sample row. The sample is not the full population. The preview emits no pagination
headers because it uses its own explicit total/sample response.

## Clone values, create identity

Cloning copies a valid rule definition and gives it a new ID, name, and timestamps.
It does not attach the old entity as Added, copy the old ID, or freeze membership.
Both definitions execute against current contact data.

The Admin-only clone checks exact stored-name uniqueness and handles the database
uniqueness race too. A successful request followed by a network retry may return
a name conflict; this endpoint is not an idempotent upsert. Updating the clone's
rules should leave the source untouched.

## The explanation must use the real evaluator

Read [SegmentDiagnosticsController.cs](../../../src/Harbor.Api/Controllers/SegmentDiagnosticsController.cs)
and [SegmentRuleShape.cs](../../../src/Harbor.Infrastructure/SegmentRuleShape.cs).
Explanation runs each condition through `SegmentCompiler` against the one scoped
contact, in SQL. It then evaluates the full rule set through the same compiler.

| Stored value | Rule | Result under existing segment semantics |
| --- | --- | --- |
| Missing plan | `NotEquals enterprise` | True |
| Missing plan | `Exists` | False |
| Empty email | `Exists` | True |
| Date exactly at boundary | `Before` | False |
| Date exactly at boundary | `After` | False |

Notice that segment `Exists` means nonnull. The junior contact `hasEmail` filter
asks whether an email is nonblank. These are two explicit contracts answering
different questions. An explanation must reproduce the segment's established
rule, not substitute the junior presence rule because it sounds similar.

Calling `.Compile()` on the expression and evaluating it in .NET would fail for
the SQL-only JSON extraction function and could create different null behavior.
Instead, the diagnostic makes at most 51 membership queries for 50 conditions
plus the overall result, sequentially on one DbContext and within one read
transaction. That bounded cost is a conscious diagnostic tradeoff.

## Exercises and evidence

Read [ContactInsightsLearningTests.cs](../../../tests/Harbor.Tests/Integration/ContactInsightsLearningTests.cs)
and [SegmentLearningTests.cs](../../../tests/Harbor.Tests/Integration/SegmentLearningTests.cs).

1. Predict a patch's final map before reading its assertions.
2. Identify the test where every individual value fits but the complete JSON does not.
3. Explain the difference between duplicate group count and member count.
4. Count the opening messages in the support-summary fixture.
5. Explain why changing a clone to `plan=free` leaves the source's enterprise members.
6. Say why an empty email passes segment Exists but fails nonblank email presence.
7. Find where a foreign contact or segment is rejected before its data is exposed.

Tomorrow, repeat the patch exercise using a different omitted key and the segment
exercise using Match=Any. On the next review, focus on the failure paths: stale
token, trim collision, unsupported stored rules, and unavailable destination.
