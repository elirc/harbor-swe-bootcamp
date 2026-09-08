# 05: Start one conversation in four ways

**Remember today:** preparing tracked entities is different from saving them.

Maya asks about an invoice. We will follow
`POST /api/workspaces/{workspaceId}/conversations`.

## Way 1: the product story

The request identifies Maya's contact and the inbox, and includes a subject and
opening body. Harbor checks that the workspace exists and that the inbox and
contact are available within it. Those references need checking even if their
IDs are valid GUID strings.

`ConversationStarter` builds the conversation and its opening contact message.
It applies applicable response/resolution deadlines, optionally selects an
assignee, and prepares webhook deliveries for applicable subscriptions.
The controller saves the changes and returns a 201 creation response.

## Way 2: a checklist for this specific request

| Question | Where the answer is established |
| --- | --- |
| Is the HTTP input valid? | API binding and request validation |
| Can this caller access the route's workspace? | Authentication and workspace authorization |
| Do the inbox and contact belong here? | Lookups in `ConversationsController.Start` |
| What must happen whenever a conversation starts? | `ConversationStarter.StartAsync` |
| When are the prepared database changes written? | The controller's `SaveChangesAsync` |
| What does the caller receive? | A mapped conversation detail with status 201 |

Open [Start](../src/Harbor.Api/Controllers/ConversationsController.cs) and
[ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs)
side by side. Trace only this checklist during the first reading.

## Way 3: read three real statements slowly

From the controller:

```csharp
var conversation = await ConversationStarter.StartAsync(
    db, inbox, contact, request.Subject, request.Body, DateTimeOffset.UtcNow,
    request.Priority ?? ConversationPriority.Normal);
await db.SaveChangesAsync();

return CreatedAtAction(nameof(GetById), new { id = conversation.Id }, conversation.ToDetailResponse());
```

The first statement coordinates creation and gives us the prepared conversation.
The starter can read the database while applying rules, but it leaves the final
save to this caller. `?? ConversationPriority.Normal` supplies the normal priority
when the request omits one.

The second statement persists the tracked database changes.

The third statement builds the creation response. It supplies the ID needed to
construct the detail URL and maps the conversation to its response shape. It is
not another database save.

## Way 4: the “prepared versus durable” table

| Moment | What can we say? |
| --- | --- |
| After `new Conversation` | A C# object exists in memory |
| After `db.Conversations.Add(...)` | EF knows it should insert this conversation |
| After the starter returns | Related start-time work has been prepared |
| After successful `SaveChangesAsync` | The related database changes have committed |
| After later successful dispatch | A subscriber may have received a webhook attempt |

“Added to the context” and “saved in SQLite” are different milestones. “Queued a
delivery” and “sent it over HTTP” are also different milestones.

The database transaction joins the saved conversation and its prepared outbox
work. It cannot promise that an external receiver observes a delivery exactly once.

## Why have a starter at all?

Chat creation and new conversations from inbound email both use this starter.
It reduces the chance that one creation path forgets assignment, deadlines, or
events. You are seeing one responsibility shared by two callers.

## Check your explanation

Could you say the conversation is saved just because the starter returned?
Would returning JSON by itself save a new object?

<details>
<summary>Check the answer</summary>

No to both. This starter leaves saving to the caller. Mapping and returning a
response describe an answer to the HTTP client; persistence requires the save.

</details>

Repeat: **prepare, save, respond; dispatch comes separately**.
Next: [read the C# symbols](06-reading-csharp.md).
