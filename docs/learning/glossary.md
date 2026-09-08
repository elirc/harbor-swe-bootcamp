# A practical glossary

| Term | Plain meaning | Harbor example |
| --- | --- | --- |
| Acceptance criterion | An observable condition a feature must satisfy | Empty overview returns five zeros |
| Aggregate query | Database work that summarizes rows | Counts by conversation state |
| API contract | What a client may rely on | Routes, fields, status codes, paging headers |
| Authentication | Establishing who is calling | Validating `X-Api-Key` |
| Authorization | Deciding what that caller may do | Workspace filter and admin-only operations |
| Boundary | A place where responsibility or trust changes | HTTP input entering application code |
| Cancellation | A request to stop unfinished work | Passing the request token to `CountAsync` |
| Claim | Identity information attached after authentication | Workspace and teammate IDs |
| Concurrency | Multiple operations overlapping in time | Two agents assigning the same conversation |
| Concurrency token | A value used to detect a stale write | `Conversation.Version` |
| Dependency injection | Supplying an object's collaborators | Controller receives `HarborDbContext` and queries |
| Domain invariant | A business condition that should always hold | A conversation has at most one kind of assignee |
| DTO | Data shape passed across a boundary | `ConversationOverviewResponse` |
| Eager loading | Loading related data along with a query | Including tags on the conversation page |
| Idempotency | Repeating an operation has the intended same effect | Re-evaluating an already recorded SLA breach |
| Index | Extra database structure that can accelerate access | Workspace/state conversation index |
| Integration test | A test exercising collaborating components | HTTP through ASP.NET into SQLite |
| IQueryable | A composable description of query work | Filter query before `ToListAsync` |
| Materialization | Turning query results into application objects | Fetching a page into a `List` |
| Migration | An ordered change to a persisted schema | Adding a table or index |
| N+1 query | One initial query followed by a query per result | A possible mistake when fetching tags in a loop |
| No tracking | Reading entities without retaining update tracking | Conversation list query |
| Observability | Evidence that helps explain system behavior | Request logs and delivery status |
| Offset pagination | Skipping rows to reach a numbered page | `Skip` and `Take` |
| Projection | Selecting only a chosen result shape | SQL counts into an overview response |
| Regression test | A test that detects a defect returning | Huge-page overflow case |
| Scope | Lifetime or access boundary, depending on context | One request's DbContext; one workspace's data |
| Snapshot | A view corresponding to a particular state | Something multiple queue requests do not guarantee |
| Tenant | A customer partition of a shared application | Workspace |
| Tie-breaker | Additional ordering key for otherwise equal rows | ID after creation timestamp |
| Transaction | Database changes committed or rolled back together | Message plus queued delivery |
| Transactional outbox | Durable outbound work saved with its triggering change | `WebhookDelivery` queued before save |

Practice: pick five terms and explain them using a request you traced. If a
definition remains abstract, find the source line or test that demonstrates it.
