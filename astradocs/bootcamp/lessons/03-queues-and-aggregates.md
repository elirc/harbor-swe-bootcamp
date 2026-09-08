# Lesson 03: queues, messages, and trustworthy counts

Stories: US-16 through US-25. Start with [Lesson 01](01-contact-queries.md) if nullable filters still feel unfamiliar. Revisit [Lesson 02](02-directories-and-settings.md) for model validation and safe response mapping.

## What an agent is trying to do

An agent asks for conversations that are still waiting for their first response. They want the highest-priority work first, a count of everything waiting, and a way to read a long thread one page at a time. This crosses three levels of data: a conversation queue, an individual conversation, and its messages.

The implementation uses the existing shared queue builder. It adds message-specific reads and aggregate responses without changing the detail endpoint's full-thread response. Check [progress](../progress.md) for the actual verification state.

## A map you can trace with a finger

```text
ConversationFilterRequest
        |
        v
ConversationQueries.BuildAsync
        |                     |
        v                     v
List: order and page          Overview: count the entire filtered set

MessageFilterRequest
        |
        v
Check conversation belongs to caller's workspace
        |
        v
Filter that conversation's messages, order, count, and page
```

Read [Requests.cs](../../../src/Harbor.Api/Contracts/Requests.cs), [ConversationQueries.cs](../../../src/Harbor.Api/Infrastructure/ConversationQueries.cs), then [ConversationsController.cs](../../../src/Harbor.Api/Controllers/ConversationsController.cs). Stop after each stage and describe the data that is still eligible to appear in the response.

## Four questions, four predicates

| User's question | Stored fact used by the query | What it does not imply |
| --- | --- | --- |
| Does this thread have a useful subject? | Subject contains a non-whitespace character | The subject describes the issue well |
| Has anyone sent the first teammate reply? | `FirstRespondedAt` is non-null | The conversation is currently closed |
| Does it have any tags? | At least one tag relationship exists | A particular tag is present |
| Does it have an SLA target? | First-response due date or resolution due date exists | The target is currently breached |

An internal note is not a first reply. Closing a conversation does not manufacture a first response. A closed conversation may therefore match `awaitingFirstResponse=true`. You can combine `state=Open` when the user wants only open work; the presence filter should not add that restriction invisibly.

Think of each optional boolean as a switch with three positions: require the fact, require its absence, or ignore the fact. Say that again using the code: `true`, `false`, or `null`.

## Why `Any` is the right question

Suppose conversation A has two tags and B has none. `hasTags=true` should return A once. We care whether the relationship exists, not how many matching relationship rows a join produces. `Any` expresses that question and lets the SQL query use existence semantics.

Practice: draw A with two arrows to tags. Then draw the requested result as a set containing A. The arrows are evidence for membership; they are not two copies of A.

## Time windows with no overlapping boundary

The updated-time window is `[UpdatedFrom, UpdatedBefore)`: include the lower endpoint and exclude the upper endpoint.

For updates at 10:00, 11:00, and 12:00 UTC, the window from 10:00 up to 12:00 includes the first two. A second window starting at 12:00 includes the third. No event belongs to both adjacent windows.

An offset is a different representation of an instant. `05:00-05:00` is the same instant as `10:00Z`. The integration test sends the offset form and checks the expected IDs stored at UTC times.

The request rejects equal or reversed bounds. This validation applies to list and overview because they consume the same request type. Creation-time bounds remain independent: a caller may require that a conversation was created last month **and** updated today.

Exercise: predict which row passes a creation window but fails an update window. Draw both timestamps for that one conversation before touching the query.

## Sorting needs a final answer for ties

`PriorityFirst` sorts by priority descending, then most recent activity descending, then ID ascending. Priority decides first; activity only compares rows whose priorities tie. ID makes the order reproducible when both other values tie.

Consider Normal updated now, Urgent updated two days ago, and Urgent updated yesterday. The urgent conversation updated yesterday comes first, then the older urgent conversation, then Normal. Recent activity does not override priority in this sort mode.

The older sort enum values keep their numbers. Adding a new choice at value 3 preserves callers that already send 0, 1, or 2. This is a small compatibility decision worth noticing before changing a public enum.

## Messages: scope the parent before reading the children

Message rows belong to a conversation. Before listing them, the endpoint verifies that the conversation exists in the authenticated workspace. An unknown conversation and another workspace's conversation both return 404. An empty array is reserved for an authorized conversation whose selected page has no messages.

Now apply `kind=Reply` or `kind=Note`, count matching messages, and page them in creation-time/ID order. Filtering after pagination would give misleading totals and potentially empty pages even though later matching messages exist.

The count endpoint deliberately answers a different question: all messages in that conversation, partitioned into replies and notes. It ignores incidental `kind` or paging query parameters. The opening contact message counts as a reply.

## Three counts that should not be confused

| Read | Membership | Page affects count? |
| --- | --- | --- |
| Conversation overview | All conversations matching the queue filters | No |
| Contact overview | All contacts in the workspace | No; it does not take contact filters |
| Message counts | All messages in the authorized conversation | No; it does not take message filters |

With two replies and one note, `/messages?kind=Reply&pageSize=1` returns one message and `X-Total-Count: 2`. `/message-counts` returns total 3, replies 2, notes 1. Both answers are correct because the questions differ.

The contact overview uses the same definition of email presence as contact filtering, including tabs and Unicode whitespace. It computes all three numbers in one aggregate query. Separate queries for total and present could observe different database states if another request writes between them.

## Read tests as worked examples

Open [QueueLearningTests.cs](../../../tests/Harbor.Tests/Integration/QueueLearningTests.cs).

1. In the subject test, underline the explicit null and blank subjects. The helper normally supplies a nonblank default, so omitting the argument would arrange the wrong fixture.
2. In the first-response test, point to the note and the close. Neither should remove that thread from the awaiting set.
3. In the SLA test, notice the resolution-only target. Testing only first-response targets would not prove the OR condition.
4. In the time test, calculate the matching IDs yourself before reading the assertion.
5. In the message test, count the opening message before adding the note and reply.
6. In the contact overview test, explain why `q=absent` must not reduce the totals.

## Deliberate debugging lab

On a disposable practice branch, change one condition at a time and predict the failure before running tests:

1. Replace `UpdatedAt < updatedBefore` with `<=`. The upper-bound row now leaks into the window.
2. Change the SLA OR to AND. A conversation with only one target now disappears.
3. Remove the message parent ownership check. The foreign-workspace test now exposes messages.
4. Move the priority sort after activity. A newly active Normal thread can jump ahead of Urgent work.

Undo each experiment immediately after observing it. These experiments are learning exercises, not requested production changes.

## Explain it again tomorrow

Use a different example: a clinic's appointment list, the total number of matching appointments, and the notes attached to one appointment. Explain why filtering, counting, sorting, and paging are separate operations. Then map each noun back to conversations and messages. This translation checks understanding beyond memorizing class names.

You are ready to move on when you can state the exact population behind any returned count, explain one boundary condition, and identify the authorization check that protects a child collection.
