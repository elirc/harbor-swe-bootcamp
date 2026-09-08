# Security and operations: own the boundary

Goal: review an endpoint as someone responsible for its data and operation.
Use synthetic workspaces and test keys while practicing.

## Review permissions before business behavior

For every endpoint, write who can call it and which workspace it may affect.
For the new overview, an admin or agent can read their own workspace; another
workspace route is forbidden, and a foreign segment is treated like a missing
segment. Counts must remain tenant-scoped even when no records are returned.

Build a small matrix: anonymous, own admin, own agent, foreign teammate; existing
own resource, existing foreign resource, random ID. Do not assume a valid API key
permits every action. Study `AuthorizationMatrixTests` and `WorkspaceScopeMatrixTests`.

Exercise: select an endpoint with a route ID and a second ID in the body. Trace
both checks. Explain why validating only the route workspace would be insufficient.

## Separate secrets, identities, and user content

API keys authenticate callers. Hashed stored keys reduce exposure if the database
is read, but returned creation responses still contain usable secrets. Teammate
IDs identify actors and are not credentials. Message bodies can contain customer
information even when they are not formally credentials.

`RequestLoggingMiddleware` logs request metadata and actor IDs without logging
API keys or request bodies. Read its position in `Program.cs`. As an advanced
exercise, inspect whether its `finally` observes the final status when an outer
exception handler later converts an exception to a ProblemDetails response.
Treat logs as evidence with an execution order, not automatic ground truth.

## Review outbound requests as another trust boundary

Webhook URLs are configuration that causes the server to make HTTP requests.
The current `[Url]` shape validation and HTTP sender do not implement a complete
outbound destination policy. An operations design should consider who configures
destinations, allowed protocols, private destinations, redirects, DNS changes,
and network egress controls. A regex alone is not a complete solution.

Exercise: write the policy for a local development installation and for an
internet-hosted multi-tenant service. List which controls belong in application
code and which belong at the network boundary. This is a design exercise; do not
probe real private services to demonstrate it.

## Learn to respond to a dependency warning

During this update, restore reported advisories for transitive OpenAPI and native
SQLite packages. The project now explicitly selects `Microsoft.OpenApi` 2.7.5
and `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13. This illustrates why updating a top-level
framework package alone does not necessarily select every desired transitive fix.
Microsoft's [OpenAPI advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc)
describes the parser fix; inspect the resolved package graph for your own build.

```powershell
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

Record when the audit ran. “No known vulnerabilities reported” is a result of
that audit and its available data, not a permanent security certification. Do
not silence a warning before understanding the affected package and runtime path.

## Write a runbook someone else can follow

For a failed local startup, record the exact command, environment, connection
string source, exception, and last known working revision. Distinguish network
restore failures, compilation failures, schema mismatches, and runtime failures.

For an outbox backlog, inspect oldest pending age, due count, attempt counts,
subscriber status, and error categories. State whether dispatch is actually being
triggered. An empty queue and a broken dispatcher can look identical if no new
events arrive during your check.

Harbor's `/health` probes database connectivity. It does not prove that webhooks
are being delivered, that a scheduler exists, or that every query is fast.
Development startup applies migrations and demo seeding; production migration
execution must be arranged separately.

Exercise: write a five-step runbook for “messages save, webhooks do not arrive.”
Include observations, one reversible remediation, verification of recovery, and
when to stop and escalate. Next: [capstones](capstones.md).
