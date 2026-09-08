# 09: Who can do what, and why did a request fail?

**Remember today:** knowing an ID is not permission to use it.

## First explanation: a badge and a room

An API key is like a badge identifying a support teammate. A workspace is like
the organization's room. Knowing another room's label does not mean the badge
opens it.

The analogy separates two questions. **Authentication** asks who is calling.
**Authorization** asks what that caller may do. Harbor stores teammate identity,
workspace, and role in the authenticated caller's claims.

The role also matters. Some configuration operations require Admin; being an
Agent in the right workspace does not authorize every operation.

## Second explanation: follow the actual checks

[ApiKeyAuthenticationHandler](../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs)
reads `X-Api-Key`, hashes it, looks up a teammate, and creates an identity if the
key matches. This lookup uses the database.

[WorkspaceScopeFilter](../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs)
compares a `workspaceId` route value with that identity's workspace. The comparison
does not look up the requested workspace, so foreign and invented workspace IDs
are rejected alike. The whole authentication process is not database-free.

A by-ID conversation lookup includes both conditions: the conversation's ID
and `User.GetWorkspaceId()`. An unknown conversation and another workspace's
conversation both produce no accessible match.

## Third explanation: sort errors by the question they answer

| Status | Question to investigate | Example in Harbor |
| --- | --- | --- |
| 400 | Could the API bind and validate this input? | `sort=999` or reversed creation bounds |
| 401 | Is there a recognized authenticated caller? | Missing or unknown API key on a protected route |
| 403 | Is this operation allowed for this caller? | Foreign workspace route or an admin-only action by an agent |
| 404 | Is there a resource visible through this lookup? | Missing or foreign conversation by ID |
| 409 | Does this conflict with another value or write? | Optimistic concurrency conflict |
| 422 | Is the request usable under the business/reference rules? | Snooze into the past or a foreign segment filter |

These are examples of this repository's contract, not a universal rule that
every API assigns each status exactly this way. Read the response's problem
details along with the status.

## One request, two IDs to check

Starting a conversation includes a workspace ID in the route and inbox/contact
IDs in the body. Permission for the route does not prove those other records
belong to it. The action checks their workspace too.

Try saying that differently: “I may work in Acme, but I cannot attach a contact
from another organization just by sending its ID.”

## Exceptions you should know exist

Health, workspace bootstrap, and public help-center endpoints allow anonymous
access by their contracts. The authenticated conversation list and overview do
not. A public article route is not an alternative way to read private conversations.

## A three-case check

Imagine your valid key belongs to workspace A. Predict the response when you
request workspace B's conversation list, a conversation belonging to B by ID,
and A's overview with no key.

<details>
<summary>Check the answers and reasons</summary>

The foreign workspace route returns 403. The foreign by-ID conversation returns
404, hiding it like an unknown resource. A protected overview request without a
key returns 401. Each failure occurs for a different reason.

</details>

Repeat: **identity first, permissions next, tenant-scoped data throughout**.
Next: [run and observe](10-run-and-observe.md).
