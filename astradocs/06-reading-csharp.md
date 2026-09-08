# 06: Read one C# line at a time

**Remember today:** first find the value, then the action, then the result.

You do not need to learn every symbol before reading Harbor. Use this page next
to the source, translating one line into an ordinary sentence.

## Names and types

From [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs):

```csharp
public Guid ContactId { get; set; }
public string? Subject { get; set; }
```

| Piece | Read it as |
| --- | --- |
| `public` | Other code can access this member |
| `Guid` | A type used for identifiers |
| `ContactId` | The name of this value: which contact |
| `{ get; set; }` | Code can read and assign this property |
| `string` | Text |
| `?` after `string` | The text may be absent (`null`) |

A GUID identifies a record. It does not grant permission to read it. `null`
means no value; it is not the literal text `"null"`, and it is different from
an empty string `""`.

Try saying: “This conversation has a contact identifier and may have a subject.”

## Choice and comparison

From the snooze rule:

```csharp
if (until <= now)
{
    throw new DomainException("Snooze time must be in the future.");
}
```

`if` asks a question. `<=` means earlier than or equal to here. If the answer is
yes, `throw` interrupts the normal flow with a domain-rule failure. A future
time does not enter this block.

Practice with `now = 12:00`: predict what happens for 11:59, 12:00, and 12:01.

<details>
<summary>Check the three predictions</summary>

11:59 fails. Exactly 12:00 also fails. 12:01 is a valid future time. A domain
unit test can check this without starting the web server.

</details>

## A small condition inside a query

```csharp
query = query.Where(c => c.State == state);
```

`c` names one conversation inside the condition. `=>` separates that input from
the expression about it. `==` compares values. The single `=` at the beginning
assigns the newly composed query back to `query`.

Read it as: “Update the query so it keeps conversations whose state matches.”
Here `query` is an `IQueryable`, so the condition is composed for database
execution later. The similar expression on a loaded list would run in memory.

## Absence and defaults

In [Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), optional filters allow
values to be absent. In the creation action:

```csharp
request.Priority ?? ConversationPriority.Normal
```

Read `??` as “use the right-hand value when the left-hand value is null.” This
means an omitted priority becomes Normal.

In `Conversation.Close`, `FirstResolvedAt ??= now` means “set this timestamp only
if it is still absent.” It preserves the first resolution across later closes.

These are different operations: `??` chooses a value; `??=` conditionally assigns one.

## Tasks and awaiting

```csharp
await db.SaveChangesAsync();
```

Read it as: “Wait for the asynchronous save operation to complete before moving
on.” The name ending in `Async` is a naming convention. The returned task and
`await` express asynchronous control flow.

`await` does not mean “start an unrelated background job and immediately return
success.” It also does not make several operations one transaction by itself.

## Where did `db` come from?

The controller declares the collaborators it needs in its constructor:

```csharp
public class ConversationsController(HarborDbContext db, ConversationQueries queries) : ControllerBase
```

`Program.cs` registers these services. ASP.NET supplies them when it creates the
controller. That arrangement is called dependency injection. For now, remember:
**the controller receives its tools instead of constructing every tool itself**.

## One-minute recap

Translate `string?`, `==`, `??`, `??=`, and `await` using the examples above.
Then close this page and explain one line from the actual controller.
Next: [data and pages](07-data-and-pages.md).
