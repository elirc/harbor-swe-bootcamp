# 02: Give each folder one job in your head

**Remember today:** start with three source projects and one test project.

## First pass: only four folders

```text
src/Harbor.Api/             Receives HTTP requests and coordinates responses
src/Harbor.Domain/          Defines business objects and behavior
src/Harbor.Infrastructure/  Works with the database and external integrations
tests/Harbor.Tests/         Checks expected behavior
```

Read the right side first. Then read the folder names. Long names become easier
when you attach them to a responsibility you already understand.

## Second pass: a support-office analogy

The **API** is like the front desk: it receives a request, checks its form and
access, coordinates the work, and returns an answer.

The **domain** is like the office's business rules: what it means to close a
conversation, whether a snooze time is acceptable, and how a reply changes state.

The **infrastructure** is like the filing and delivery equipment: database
storage, query translation, and sending webhook requests.

The **tests** rehearse situations and compare what happened with what should happen.

The analogy has a limit: the API is not merely a forwarding desk. Controllers
currently query the database directly and coordinate several rules. Do not assume
every request passes through four separate objects in a fixed order.

## Third pass: one real file per responsibility

| Open | Look for only this | Say it in plain English |
| --- | --- | --- |
| [Program.cs](../src/Harbor.Api/Program.cs) | `AddDbContext` and `MapControllers` | Connect the application's parts and expose routes |
| [ConversationsController.cs](../src/Harbor.Api/Controllers/ConversationsController.cs) | `List` | Handle the conversation-list HTTP operation |
| [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs) | `Snooze` | Reject a snooze time that is not in the future |
| [HarborDbContext.cs](../src/Harbor.Infrastructure/HarborDbContext.cs) | `Conversations` | Give EF access to stored conversation records |
| [ConversationTests.cs](../tests/Harbor.Tests/Unit/ConversationTests.cs) | `Snooze_InThePast_Throws` | Check the rejected-snooze behavior |

Open one file at a time. It is fine to ignore the rest of a large file during
this pass. Search for the method name rather than reading from the first line.

## A confusing detail: two places say “Infrastructure”

`src/Harbor.Infrastructure/` is a whole project. It contains database and
integration code.

`src/Harbor.Api/Infrastructure/` is a folder inside the API project. It contains
helpers for that API, including authentication, paging, and conversation queries.

Those are different locations. For example,
[ConversationQueries.cs](../src/Harbor.Api/Infrastructure/ConversationQueries.cs)
accepts an API request type, so it lives with the API.
[SegmentQueries.cs](../src/Harbor.Infrastructure/SegmentQueries.cs) works with
stored segment rules and database queries, so it lives in the infrastructure project.

## Other folders can wait

`Contracts` contains request and response shapes. `Migrations` contains database
schema changes. `docs` contains reference and learning material. `.github` contains
automation configuration. You can return to them when a task leads there.

## A tiny navigation exercise

Find the place that defines whether snoozing into the past is allowed. Then
find the test that checks it. Finally, find the HTTP action that calls the rule.

<details>
<summary>Check the three stops</summary>

`Conversation.Snooze` contains the rule. `ConversationTests.Snooze_InThePast_Throws`
checks it directly. `ConversationsController.ChangeState` calls `Snooze` for a
snooze request that includes a time. The domain exception is translated at the
API boundary into a 422 problem response.

</details>

Repeat: **the controller handles the operation; the domain method expresses the
behavior; the test checks an expectation.** Next: [pictures](03-picture-book.md).
