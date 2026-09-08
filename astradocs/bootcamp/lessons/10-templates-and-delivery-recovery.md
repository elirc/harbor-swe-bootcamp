# 10: Reuse the workflow and preserve the evidence

Stories: M26-M30. Verification status is recorded in the [tracker](../progress.md). This lesson connects a small text-rendering feature to the larger question of what happens when a side effect fails.

## Preview is a calculation; send is a workflow

A teammate chooses a canned reply containing `Hello {{contact.name}}, I am {{teammate.name}}.` Preview should show the rendered text without adding a message. Send should render the current template and use the same behavior as an ordinary teammate reply: record the author, update conversation activity and first-response milestones, detect an SLA breach if appropriate, and stage webhook deliveries.

Read [CannedReplySendingController.cs](../../../src/Harbor.Api/Controllers/CannedReplySendingController.cs). Both actions load an owned conversation, canned reply, contact, and authenticated teammate. Preview uses untracked reads. Send tracks the conversation because it will change it. A missing conversation returns 404; an unavailable related canned reply cannot be used to cross workspace boundaries.

Do not imagine preview as a reservation. The canned reply can change between preview and send. Sending renders current data again. Repeating send currently creates another message. S01's later conversation-creation retry contract does not automatically make every POST idempotent.

## Render once, with a small language

Read [CannedReplyRenderer.cs](../../../src/Harbor.Domain/CannedReplyRenderer.cs). The renderer accepts four names: `contact.name`, `contact.email`, `teammate.name`, and `conversation.subject`. Missing optional values become empty strings. Token names are case-sensitive; outer whitespace inside the double braces is trimmed. Unknown names and malformed double-brace syntax are rejected. The final body is limited to 20,000 characters.

The renderer scans the original template once. It appends a token's value without interpreting that value as a new template. If the contact's name literally contains `{{teammate.name}}`, that sequence stays literal in the result. Running a series of string replacements could accidentally interpret a previously substituted value in a later replacement pass.

Explain this with a printer: the template supplies instructions, the database values supply ink. Ink does not become another instruction sheet. Now explain it with indices: advance through the source string, locate an opening delimiter and its closing delimiter, look up the permitted token, append the value, then continue after the source delimiter. The output buffer is never the next input to the parser.

The returned used-token list is distinct and sorted. Repeating a placeholder twice repeats its value in the body but lists its name once in metadata. Preview can legitimately render blank; send rejects a blank rendered reply because the message workflow requires meaningful content.

## Share behavior at the point where behavior begins

Read [MessageAppending.cs](../../../src/Harbor.Api/Infrastructure/MessageAppending.cs) and its callers in [ConversationsController.cs](../../../src/Harbor.Api/Controllers/ConversationsController.cs) and the canned-reply controller.

`StageAsync` runs after caller-specific validation. It prepares the message, applies the email message-ID convention when needed, calls the conversation's domain behavior, stages the message and SLA effects, and invokes the webhook publisher. The caller commits those database changes together.

This shared helper prevents a template send from quietly forgetting first response or webhooks. It is deliberately staging work; saving inside it would make it harder to include other effects in one atomic transaction later. Authorization still belongs at the entry point, where the caller and related resources are known.

Trace a late first teammate reply: render text, build a TeammateReply using the authenticated teammate, stage it, set the first-response timestamp if absent, detect the missed deadline, stage the webhook delivery, save. A second reply changes activity but does not reset the first-response timestamp. Explain why copying only `db.Messages.Add` into a new endpoint would be incomplete.

## Bounded dispatch: filter before taking slots

Suppose a workspace has 201 old pending deliveries attached to a paused subscription and five ready deliveries attached to an active subscription. A dispatch limit of two should send two ready deliveries. It should not load the oldest two paused rows and return no useful work.

Read [WebhookQueries.cs](../../../src/Harbor.Infrastructure/WebhookQueries.cs) and [WebhookDispatcher.cs](../../../src/Harbor.Infrastructure/WebhookDispatcher.cs). Workspace ownership, Pending status, active subscription, and due time determine eligibility in SQL. Order eligible rows by creation time and ID, then take the requested batch. M28 accepts limits from 1 through 200, default 50.

The same idea appeared in junior pagination: ordering and filtering describe the population before slicing it. Here the slice represents work to attempt, so getting the order wrong can starve ready work behind paused rows.

## Redrive preserves the failed delivery

M29 creates a new delivery linked to a failed original. It does not reset the original's attempt count, overwrite its error, or regenerate its payload. The child has a fresh delivery ID, zero attempts, Pending status, and the exact original payload bytes as stored text.

The unique nullable `OriginalDeliveryId` index permits many ordinary deliveries with no parent and only one redrive child for a particular original. Repeating a redrive request returns that existing child, including after it succeeds. A child cannot itself be redriven through this endpoint. The first redrive requires an active subscription and a failed original; a retry can still retrieve its already-created child if the subscription was later paused.

Explain the difference between identities: the transport delivery ID is new, but the copied payload can contain the old event ID. A receiver's event-deduplication policy may therefore affect how it treats the retry. Preserving the payload is intentional; silently inventing a new event would rewrite what was originally emitted.

Read [WebhookOperationsController.cs](../../../src/Harbor.Api/Controllers/WebhookOperationsController.cs). The insert handles the known uniqueness race by looking for the existing child; unrelated database errors are not reclassified as successful retries. A uniqueness constraint is the final guard when two requests both observe that no child exists.

## Health numbers need denominators

Pending deliveries fall into three disjoint categories: ready because active and due, deferred because active and future-due, or paused because their subscription is inactive. The terminal failure rate is `Failed / (Succeeded + Failed)`, rounded to four decimal places. Pending work is excluded from that denominator. With no terminal results, the rate is null rather than a made-up zero.

For four Pending, three Succeeded, and one Failed, the failure rate is 0.25. It is not 1/8. Pausing a subscription can change ready/deferred/paused counts without changing total delivery count or the terminal rate. The response includes evaluation time and useful oldest/latest timestamps, but no payloads, secrets, destination URLs, or raw error text.

The aggregate is read-only. Reading health never sends a webhook or consumes an assignment-style rotation pointer. Monitoring should describe work, not perform work as a surprise side effect.

## Concurrency does not imply exactly-once HTTP

At this midlevel stage, the dispatcher sends HTTP before saving the result. A database version conflict can reject a stale outcome write after two workers have both sent. The original source comment overstated this protection; it was corrected during this implementation.

Even a later durable lease cannot prove exactly-once external effects. A receiver may accept the request just before the sender crashes, leaving the local outcome unknown. S03 and S04 add claims and attempt evidence; receiver deduplication remains a separate concern. Distinguish work ownership, recorded outcomes, and external effects whenever you make reliability claims.

## Practice and evidence

Read [CannedReplyRendererTests.cs](../../../tests/Harbor.Tests/Unit/CannedReplyRendererTests.cs), [CannedReplyLearningTests.cs](../../../tests/Harbor.Tests/Integration/CannedReplyLearningTests.cs), and [WebhookLearningTests.cs](../../../tests/Harbor.Tests/Integration/WebhookLearningTests.cs).

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~CannedReplyRendererTests|FullyQualifiedName~CannedReplyLearningTests|FullyQualifiedName~WebhookLearningTests'
```

Predict these results before reading assertions: a placeholder-shaped contact name, previewing a blank optional subject, dispatching two slots behind 201 paused rows, repeating redrive after the child succeeds, and calculating health with zero terminal deliveries.

Write a learner journal entry with four sentences: the rule you expected, the fixture that challenges it, the assertion that proves it, and a failure the assertion does not cover. For the sequential redrive test, explicitly note that repeated HTTP calls are not proof of every concurrent insert schedule. Tomorrow, explain why shared message staging and preserved delivery history both help maintainers understand what actually happened.
