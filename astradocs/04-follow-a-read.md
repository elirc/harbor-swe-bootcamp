# 04: Follow one read in four ways

**Remember today:** building a query is different from fetching its result.

We will follow this route shape:

```text
GET /api/workspaces/{workspaceId}/conversations?state=Open&pageSize=2
```

`{workspaceId}` is a placeholder here. The [runnable guide](10-run-and-observe.md)
obtains the real value for you.

## Way 1: tell the story

Ada asks for open conversations in her workspace, two at a time. Harbor checks
her identity and workspace access. The controller receives a filter object with
`State = Open` and `PageSize = 2`.

The shared query component describes which conversations match. The controller
adds ordering and tags. The paging helper counts the matches and fetches one page.
The controller turns the result into response objects and returns them.

## Way 2: put the handoffs in a table

| Step | Actual code to find | What it is responsible for |
| --- | --- | --- |
| Identify the caller | [ApiKeyAuthenticationHandler](../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs) | Match the key and construct the caller's identity |
| Check the workspace route | [WorkspaceScopeFilter](../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs) | Reject another workspace's route |
| Receive the operation | [ConversationsController.List](../src/Harbor.Api/Controllers/ConversationsController.cs) | Check the workspace, coordinate query and response |
| Choose membership | [ConversationQueries.BuildAsync](../src/Harbor.Api/Infrastructure/ConversationQueries.cs) | Add workspace and requested filter conditions |
| Choose order | `ConversationQueries.Order` in that same file | Order by the selected timestamp, then ID |
| Fetch a page | [Paging.ToPageAsync](../src/Harbor.Api/Infrastructure/Paging.cs) | Count matches, write headers, fetch bounded rows |
| Shape the answer | [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs) | Map entity values into the response contract |

This table describes a successful request. Missing credentials, invalid input,
or unavailable resources can stop it earlier.

## Way 3: translate a real code fragment

From `ConversationQueries.BuildAsync`:

```csharp
if (filter.State is { } state)
{
    query = query.Where(c => c.State == state);
}
```

First line: “If a state filter was supplied, call that value `state`.”

Middle line: “Add a condition: keep conversations whose state equals that value.”

Last brace: “That conditional piece is finished.”

Nothing in this fragment fetches the conversations. It adds a condition to the
query. In the paging helper, `CountAsync` executes the count and `ToListAsync`
fetches the page. If the requested offset is already past the count, the helper
returns an empty page without fetching rows.

The query builder can execute a segment lookup when a segment filter is supplied.
So “it builds a query” does not mean every line in that method is free of I/O.

## Way 4: narrate the values

Imagine there are five open conversations in Ada's workspace. The filter means
“open and in this workspace.” The count is five. Page size two means at most two
conversation summaries are returned in the array. `X-Total-Count` is still five.

The overview uses the same membership query but returns counts for all five.
It does not obtain its answer by counting only the page's two response objects.

## Check one idea

If you add `.Where(...)`, have you definitely sent SQL? If the body has two rows,
does that mean there are only two matches?

<details>
<summary>Check your explanation</summary>

Adding `Where` to this `IQueryable` composes a condition. A later executing call
does the database work. Two returned rows can be one page of a larger matching
set; the paging headers describe that set.

</details>

Say it once more: **choose the set, choose the order, fetch the page**.
Next: [follow a write](05-follow-a-write.md).
