# C# through code you can run

Prerequisite: you can run one test. Goal: read the signatures and expressions on
a request path without guessing what they mean.

## Records describe requests; entities carry behavior

In [Requests.cs](../../src/Harbor.Api/Contracts/Requests.cs),
`StartConversationRequest` is a record whose constructor parameters describe
input. Attributes such as `[Required]` and `[MaxLength]` participate in API input
validation. A valid shape still might reference an inbox in another workspace;
that requires a separate lookup and rule check.

In [Conversation.cs](../../src/Harbor.Domain/Entities/Conversation.cs), a class
holds persisted state and methods such as `Snooze`, `Close`, and `RegisterMessage`.
The methods express behavior that other callers should reuse. Public setters
mean the compiler does not enforce every invariant; tests and call-site discipline
still matter. Encapsulation is a possible future refactor, not a guarantee the
current model already provides.

Exercise: classify `Body`, `State`, `SnoozedUntil`, and `Version` as input,
persisted state, or concurrency metadata. Some values appear in multiple layers;
explain why a request type need not expose every entity property.

## Null has a meaning you must name

`DateTimeOffset?` means the value can be absent. A missing `FirstRespondedAt`
means no first teammate reply has been recorded. A missing `CreatedFrom` means
there is no lower bound on the query. The syntax is the same; the semantics differ.

```csharp
if (filter.CreatedFrom is { } from)
{
    query = query.Where(c => c.CreatedAt >= from);
}
```

`is { } from` checks for a non-null value and names it. `??` supplies a fallback;
`??=` assigns only when the left side is null. Find `FirstResolvedAt ??= now` and
explain why closing, reopening, and closing again preserves the first resolution.

Exercise: write a tiny unit test that closes twice at two distinct times. Assert
both the first-resolution timestamp and the most recent `ClosedAt` timestamp.
Predict which stays fixed and which changes before looking at existing tests.

## Async is a control-flow tool

`Task<List<T>>` represents work whose result will be a list. `await` waits for
that result without requiring the caller to block a thread during asynchronous
I/O. It does not make the database faster, make operations atomic, or permit
parallel use of a single `DbContext`.

Read `ConversationQueries.BuildAsync`: the optional segment lookup executes
asynchronously, while most statements only compose an `IQueryable`. The return
type therefore wraps a **query**, not a list of loaded conversations.

The controller's `CancellationToken` is forwarded to query execution. A
cancelled request can ask the database work to stop. Cancellation is cooperative;
it is not a transaction rollback promise for arbitrary completed side effects.

Exercise: circle every `await` on the list path. For each, name the result type
and whether any database work happens. Explain why replacing `await` with
`.Result` would make the code harder to operate safely.

## Interfaces express a useful boundary

[IWebhookSender](../../src/Harbor.Infrastructure/WebhookSender.cs) allows the
dispatcher to request an HTTP delivery without knowing how it is sent. Tests
substitute a fake to control success and failure. The interface buys a concrete
testing boundary around an external side effect.

`ConversationQueries` is a concrete class registered in dependency injection.
It does not need an interface just to qualify as well-designed. A second
implementation or a useful substitution need would justify reconsidering that.

Exercise: explain why mocking every `DbSet` is less useful for SQL-translation
tests than replacing the actual network sender in a webhook test.

## Lambdas have an execution context

`c => c.CreatedAt >= from` is a small function-shaped expression. Used with
`IQueryable`, it can become part of SQL. Used with a loaded `List`, it runs in
memory. Similar-looking C# can have very different cost and translation behavior.

Exercise: locate the first `ToListAsync` on a request path and explain what
crosses from database to application memory there. Next: [trace a request](request-tracing.md).
