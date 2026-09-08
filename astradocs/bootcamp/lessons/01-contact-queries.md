# Lesson 01: Turn a contact request into a database question

**Stories:** US01–US05. See the [tracker](../progress.md) for implementation and verification evidence.

## The user problem

A support teammate knows a customer's external reference, or wants contacts that have an email address or a previous conversation. The existing contact directory searches names and email addresses. We are extending its query without changing who can access each workspace.

Start by reading [ContactsController](../../../src/Harbor.Api/Controllers/ContactsController.cs), [request contracts](../../../src/Harbor.Api/Contracts/Requests.cs), and [Paging](../../../src/Harbor.Api/Infrastructure/Paging.cs). Read the current list method before looking at its new filters.

The implementation now keeps membership conditions in
[ContactQueries](../../../src/Harbor.Api/Infrastructure/ContactQueries.cs), and
the executable examples live in
[ContactLearningTests](../../../tests/Harbor.Tests/Integration/ContactLearningTests.cs).
Run `dotnet test --filter "FullyQualifiedName~ContactLearningTests"` to exercise
this lesson. On this Windows machine, use the SDK path recorded in the journal
if `dotnet` is not on PATH.

## One request, explained three ways

Request: `GET /api/workspaces/{workspaceId}/contacts?q=crm&hasEmail=false&pageSize=1`.

**In ordinary language:** find this workspace's contacts whose searchable details contain “crm” and who have no usable email address. Return the first result, but tell me how many contacts matched before paging.

**As a sequence:** authorize the workspace → bind query parameters → start a scoped database query → add search → add email-presence restriction → order → count → fetch one page → map responses.

**As a predicate:** `belongsToWorkspace AND matchesSearch AND lacksEmail`. Within matchesSearch, name, email, and external ID are alternatives joined by OR. The workspace restriction surrounds all of those alternatives.

An `IQueryable<Contact>` is a description of work for the database. Adding `Where` extends that description. `ToListAsync`, `CountAsync`, or the paging helper executes it. Loading every contact first and filtering a C# list afterward would change the cost and pagination behavior.

## Predict the results

| Contact | Email | External ID | Conversations |
| --- | --- | --- | --- |
| Ada | ada@example.test | CRM-10 | Two |
| Bea | null | CRM-11 | None |
| Cam | spaces only in a legacy row | null | One Closed |
| Dev | dev@example.test | null | None |

- `q=crm` matches Ada and Bea.
- `hasEmail=false` matches Bea and Cam. Null, empty text, and whitespace are all absent under this feature's contract.
- `hasConversations=true` matches Ada and Cam. A Closed conversation still counts as history.
- `q=crm&hasEmail=false` matches Bea.
- Omitted presence filters apply no restriction. `false` is an actual requested restriction, not the same thing as omitted.

The API rejects invalid email input on normal writes. A test can arrange legacy null/empty/whitespace rows directly in SQLite to verify how a read handles existing data.

## Why nullable booleans matter

A plain `bool` has two values. `bool?` has three: true, false, and null. A query filter needs all three meanings: require presence, require absence, and leave it unrestricted. If you write `if (filter.HasEmail == true)` and stop there, a caller asking for false receives an unfiltered result.

The new contact filters use three-state behavior. Do not copy it blindly into older conversation filters: existing `Unassigned=false` deliberately means no restriction. Product contracts determine the meaning; identical C# types do not guarantee identical semantics.

## Why Any is better than counting joined rows here

The question is “does this contact have at least one conversation?” `Any` expresses existence. Ada's two conversations should still produce one contact result. A join followed by paging can accidentally duplicate Ada, inflate totals, or force a later distinct operation to repair the wrong intermediate shape.

Keep the workspace predicate in the correlated conversation query too. Explain which contact the inner query refers to and which workspace its conversations must belong to.

## The write-side change

US05 trims only outside whitespace from contact names on creation and update. `"  Ada  Lovelace  "` becomes `"Ada  Lovelace"`; the two internal spaces stay. Case and Unicode stay. Name trimming does not silently normalize email or external IDs.

Request validation still runs before the controller. A raw name over the maximum length is rejected even if trimming would make it shorter. A whitespace-only required name is invalid, not a valid empty contact name.

## Exercise before reading the test results

Predict what happens if the external-ID OR condition is placed outside the workspace predicate. Create a second workspace with a matching external ID and explain what assertion should catch that mistake.

Predict what happens if paging runs before `hasEmail=false`. Which contact might occupy page one before filtering, and why would the reported total be wrong?

Explain why a contact with two conversations should appear once. Then explain the same fact by drawing one contact box pointing to two conversation boxes.

## Answers to check yourself

The foreign-workspace match must never appear. Scope and the entire search expression must remain ANDed. All membership filters must run before count and paging so both the header and rows describe the same set. Existence checks answer a yes/no question without multiplying the outer contact rows.

Tomorrow, trace only the false and null cases. The following day, start from the failing request for an invalid name and follow how validation prevents the controller write. These are different routes through the same codebase knowledge.

## A database detail worth testing, not memorizing

Whitespace can be a normal space, a tab, a newline, or a Unicode space. The API contract treats all-whitespace text as absent. SQLite's default `trim(text)` removes ordinary spaces; it does not automatically reproduce every character that .NET treats as whitespace.

That gives us a useful test design: arrange a legacy value containing only a tab and newline, then request both the true and false presence filters. Exactly one must include it, and it must be the false filter. Repeat with a Unicode space and with a real letter surrounded by whitespace.

The important habit is to test the persistence boundary. A C# unit test of `string.IsNullOrWhiteSpace` cannot prove that the SQL translation has the same behavior. The implementation will keep filtering in SQL while specifying which characters to trim explicitly.
