# API reference

Every endpoint, its authorization, its shapes, and the status codes it returns.
Routes and codes here were read from the controllers in `src/Harbor.Api`, not
from prose — where behavior is subtle the controller is named.

## Conventions

- **Base**: all routes are relative to the host (dev: `http://localhost:5230`).
- **Auth column**:
  - _Anonymous_ — no API key.
  - _Any_ — any authenticated teammate (Agent or Admin).
  - _Admin_ — `[Authorize(Roles = "Admin")]`.
  - _Self/Admin_ — the teammate themselves, or an admin acting on anyone.
- **Enums** serialize as strings. `WebhookEventType` uses dotted wire names
  (`conversation.created`); all others use their member name (`Open`, `Urgent`,
  `Available`, `Draft`, …).
- **Errors** are RFC 7807 `application/problem+json`. The status table in each
  section lists only the codes that endpoint can produce beyond the universal
  ones: **401** (missing/unknown key) on every non-anonymous route, **403** on
  every `{workspaceId}`-scoped route called for a foreign workspace, and **400**
  for malformed input or a bad query value.
- **Pagination**: every list endpoint accepts `?page=` (1-based) and
  `?pageSize=` (default 50, max 200) and returns `X-Total-Count`, `X-Page`,
  `X-Page-Size`, `X-Total-Pages` headers. Bodies are plain arrays.

## Enumerations

| Enum | Values |
| --- | --- |
| `ConversationState` | `Open`, `Snoozed`, `Closed` |
| `ConversationPriority` | `Low`, `Normal`, `High`, `Urgent` |
| `MessageKind` | `Reply`, `Note` |
| `AuthorType` | `Contact`, `Teammate` |
| `MessageChannel` | `Chat`, `Email` |
| `TeammateRole` | `Agent`, `Admin` |
| `TeammateAvailability` | `Available`, `Away` |
| `ArticleStatus` | `Draft`, `Published` |
| `AssignmentKind` | `Auto`, `Manual` |
| `SlaBreachKind` | `FirstResponse`, `Resolution` |
| `WebhookEventType` | `conversation.created`, `conversation.assigned`, `conversation.closed`, `message.created` |
| `WebhookDeliveryStatus` | `Pending`, `Succeeded`, `Failed` |
| `SegmentMatch` | `All`, `Any` |
| `SegmentOperator` | `Equals`, `NotEquals`, `Contains`, `NotContains`, `StartsWith`, `EndsWith`, `Exists`, `NotExists`, `Before`, `After` |
| `ReportInterval` | `Hour`, `Day`, `Week` |

## Health

| Method | Route | Auth |
| --- | --- | --- |
| GET | `/health` | Anonymous |

Runs a `SELECT 1` database probe. Returns **200** when healthy, **503** when the
probe fails.

```json
{
  "status": "ok",
  "name": "harbor-net",
  "version": "1.0.0+<commit-sha>",
  "utcNow": "2026-07-17T15:38:20.71+00:00",
  "database": { "healthy": true, "durationMs": 120, "error": null }
}
```

`version` is the assembly informational version. When the database is
unreachable, `status` is `"unhealthy"`, `database.healthy` is `false`, and
`database.error` is the string `"unreachable"` (details go to the operator's
logs, not the response).

## Workspaces

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces` | Anonymous | Bootstrap; returns the first admin's API key |
| GET | `/api/workspaces` | Any | Lists the caller's own workspace |
| GET | `/api/workspaces/{workspaceId}` | Any | Workspace detail |

`POST` body: `{ "name", "adminName", "adminEmail" }`. Response
(`CreateWorkspaceResponse`): `{ "workspace": {...}, "admin": {...}, "apiKey" }`.
**The `apiKey` is shown only here.** → **201**.

`WorkspaceResponse`: `{ id, name, createdAt }`.

## Inboxes

| Method | Route | Auth |
| --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/inboxes` | Admin |
| GET | `/api/workspaces/{workspaceId}/inboxes` | Any |
| GET | `/api/workspaces/{workspaceId}/inboxes/{id}` | Any |
| PUT | `/api/workspaces/{workspaceId}/inboxes/{id}/name` | Admin |
| PUT | `/api/workspaces/{workspaceId}/inboxes/{id}/auto-assign` | Admin |

`POST` body (`CreateInboxRequest`): `{ "name", "firstResponseSlaMinutes"?,
"autoAssign"?=false, "emailAddress"? }`. `emailAddress` must be unique within the
workspace → **409** on collision (case-insensitive); an invalid address → **400**.
`InboxResponse`: `{ id, workspaceId, name, firstResponseSlaMinutes, autoAssign,
emailAddress, createdAt }`. Create → **201**.

Inbox list accepts trimmed, case-insensitive `q` across name/email and optional
`autoAssign=true|false`; omission leaves either unrestricted. Filters compose
before count/paging. Invalid boolean input returns **400**.
Rename accepts `{ "name": "New name" }` (required, raw maximum 200), trims outer
whitespace, and changes only the name. Auto-assignment accepts an explicit
`{ "autoAssign": true }` or false; missing/null gives **400**. Both return
`InboxResponse` (**200**), missing/scoped-out ID **404**, non-admin **403**.
Toggling affects future conversation starts and preserves existing assignments.

## Contacts

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/contacts` | Any | |
| GET | `/api/workspaces/{workspaceId}/contacts` | Any | `q` searches name/email/external ID; optional `hasEmail`, `hasExternalId`, `hasConversations` |
| GET | `/api/contacts/{id}` | Any | |
| PUT | `/api/contacts/{id}` | Any | |
| DELETE | `/api/contacts/{id}` | Admin | **409** if the contact has conversations |

Contact search is case-insensitive substring matching across the three fields
with OR semantics. Search and presence/history filters combine with AND, before
counting and pagination. Each new boolean filter accepts `true` (present), `false`
(absent), or omission (unrestricted); malformed boolean text gives **400**.
Email/external text is absent when null, empty, or entirely .NET whitespace,
including tabs and Unicode spaces. Conversation history includes every state,
including Closed, and never duplicates a contact with several threads. Results
are ordered by name then ID. For example,
`?q=crm&hasEmail=false&hasExternalId=true&page=2&pageSize=1` counts all matching
contacts before selecting its second row.

Create/update trims outside whitespace from contact names while preserving case,
Unicode, and internal spaces. Required/maximum-length validation applies to the
raw request before trimming; invalid names do not create or update a contact.

`POST`/`PUT` body: `{ "name", "email"?, "externalId"?, "attributes"? }` where
`attributes` is a flat `{ "key": "value" }` map (null values are dropped).
`ContactResponse`: `{ id, workspaceId, name, email, externalId, attributes,
createdAt, lastSeenAt }`. Create → **201**.

## Teammates

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/teammates` | Admin | Unique email; returns API key |
| GET | `/api/workspaces/{workspaceId}/teammates` | Any | |
| GET | `/api/teammates/{id}` | Any | |
| PUT | `/api/teammates/{id}/availability` | Self/Admin | |

`POST` body: `{ "name", "email", "role"?=Agent }`. Duplicate email → **409**.
Response (`TeammateCreatedResponse`): `{ id, workspaceId, name, email, role,
createdAt, apiKey }` — **the `apiKey` is shown only here** → **201**.

`PUT availability` body: `{ "availability", "capacityLimit"? }`. An agent may set
their own; setting another teammate's when not an admin → **403**
(`TeammatesController.UpdateAvailability`).

`TeammateResponse`: `{ id, workspaceId, name, email, role, availability,
capacityLimit, createdAt }` — no key field.

Teammate list accepts trimmed case-insensitive `q` across name/email,
`availability=Available|Away`, and `role=Admin|Agent`. Omitted fields impose no
restriction; conditions combine before count/paging. Malformed or undefined enum
values return **400**. Availability is the stored setting, not a capacity check.

## Teams

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/teams` | Admin | Unique name → **409** |
| GET | `/api/workspaces/{workspaceId}/teams` | Any | |
| GET | `/api/teams/{id}` | Any | |
| POST | `/api/teams/{id}/members` | Admin | Idempotent; foreign teammate → **422** |
| DELETE | `/api/teams/{id}/members/{teammateId}` | Admin | |

`TeamResponse`: `{ id, workspaceId, name, memberIds, createdAt }`.
Add-member body: `{ "teammateId" }`.

Team list accepts trimmed case-insensitive substring `q` on name, preserving
`memberIds` in each returned row. Filtering precedes count and paging.

## Conversations

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/conversations` | Any | Starts via `ConversationStarter` |
| GET | `/api/workspaces/{workspaceId}/conversations` | Any | Filters below |
| GET | `/api/workspaces/{workspaceId}/conversations/overview` | Any | Counts for the full filtered queue |
| GET | `/api/conversations/{id}` | Any | Full thread |
| POST | `/api/conversations/{id}/messages` | Any | Reply or note |
| POST | `/api/conversations/{id}/state` | Any | Open / snooze / close |
| POST | `/api/conversations/{id}/assignment` | Any | Assign / unassign |
| GET | `/api/conversations/{id}/assignment-events` | Any | Audit trail |
| PUT | `/api/conversations/{id}/priority` | Any | Re-stamps SLA |
| GET | `/api/conversations/{id}/sla-breaches` | Any | |
| GET | `/api/conversations/{id}/suggested-articles` | Any | `?limit=`, default 5 |

### Start

Body (`StartConversationRequest`): `{ "inboxId", "contactId", "subject"?,
"body", "priority"? }`. Unknown inbox or contact in this workspace → **422**.
Returns `ConversationDetailResponse` → **201**. Auto-assign, SLA stamping, and
webhooks all fire here.

### List filters

`GET /api/workspaces/{workspaceId}/conversations` accepts any combination of:

| Param | Meaning |
| --- | --- |
| `state` | `Open` / `Snoozed` / `Closed` |
| `createdFrom` | Inclusive creation-time lower bound (ISO 8601 timestamp) |
| `createdBefore` | Exclusive creation-time upper bound (ISO 8601 timestamp) |
| `sort` | `RecentActivity` (default), `OldestCreated`, `NewestCreated`, or `PriorityFirst` |
| `updatedFrom`, `updatedBefore` | Inclusive lower / exclusive upper update-time bounds |
| `hasSubject` | True: nonblank subject; false: null/empty/whitespace-only |
| `awaitingFirstResponse` | True: `FirstRespondedAt` absent; false: present, regardless of state |
| `hasTags` | True: any tag relationship; false: none |
| `hasSlaTarget` | True: either due timestamp exists; false: neither exists |
| `inboxId`, `contactId`, `assignedTeammateId`, `assignedTeamId` | Exact match |
| `unassigned=true` | Neither teammate nor team assigned |
| `priority` | `Low` / `Normal` / `High` / `Urgent` |
| `channel` | `Chat` / `Email` |
| `segmentId` | Conversations whose contact currently matches the segment (unknown segment → **422**) |
| `tag` | Exact tag name, case-insensitive |
| `q` | Case-insensitive search over subject and reply bodies (notes excluded) |
| `slaBreached=true` | A first-response or resolution target was missed |

Default order is most recent activity (`LastMessageAt` descending).
`OldestCreated` sorts `CreatedAt` ascending; `NewestCreated` sorts it descending.
`PriorityFirst` sorts priority descending, then `LastMessageAt` descending.
Every mode breaks ties by `Id` ascending. Omitted date bounds are unrestricted;
include a timezone in timestamps. Equal/reversed bounds, malformed dates, and
invalid sort values return **400** validation ProblemDetails. Existing
`unassigned=false` and `slaBreached=false` values apply no restriction.

The four new optional boolean filters use false as the complement and omission
as unrestricted; malformed values return **400**. Subject whitespace follows
.NET whitespace semantics, including tabs and Unicode whitespace. Notes do not
end the first-response wait. Updated-time bounds validate independently of
creation-time bounds, and all queue filters also apply to queue overview.

Example: `?createdFrom=2026-01-01T00:00:00Z&createdBefore=2026-02-01T00:00:00Z&sort=OldestCreated`.

### Queue overview

`GET /api/workspaces/{workspaceId}/conversations/overview` accepts the same filter
record as the list. `page`, `pageSize`, and valid `sort` values do not change the
counts. Invalid sort input is still rejected by the shared contract. Returns
**200** with a `ConversationOverviewResponse`, for example:

```json
{ "total": 12, "open": 7, "snoozed": 2, "closed": 3, "unassigned": 4 }
```

Counts cover all matching conversations. State counts partition `total`;
`unassigned` overlaps those states. All active filters apply to every count, so
`state=Open` yields zero snoozed/closed counts. An empty result returns five zeros.
The overview has no pagination headers because its body is one aggregate object.

Missing/unknown API keys return **401**; another workspace route returns **403**;
an unknown or foreign segment returns the same **422** problem. Both Admin and
Agent may read their own workspace. Date-range/sort validation is shared with
the list. No new permission is required.

The overview computes its counts in one SQL aggregate statement. Separate list
and overview requests may observe different data under writes; neither offers
a stable export snapshot. See the [worked lesson](learning/worked-feature.md).

### Message reads

`GET /api/conversations/{id}/messages` returns paged `MessageResponse` rows,
ordered by creation time then ID. Optional `kind=Reply|Note` filters before
count/paging; undefined or malformed values return **400**. The opening contact
message is included. The existing conversation detail still returns all messages.

`GET /api/conversations/{id}/message-counts` returns `{ total, replies, notes }`
over the entire conversation; incidental paging/kind parameters are ignored.
Both reads allow any authenticated teammate in the conversation's workspace;
unknown and foreign parent IDs return **404**, even when a page would be empty.

`GET /api/workspaces/{workspaceId}/contacts/overview` returns
`{ total, withEmail, withoutEmail }` for the whole workspace, using the contact
filter's nonblank email definition. It ignores incidental contact filters and
paging parameters. An empty workspace returns three zeros.

### Add message

Body (`AddMessageRequest`): `{ "authorType", "authorId", "kind", "body" }`.

- A `Contact` author must be the conversation's own contact → else **422**
  (`Invalid contact author`).
- A `Teammate` author must belong to this workspace → else **422**
  (`Unknown teammate`).
- A `Note` may only be authored by a teammate → else **422**
  (`Invalid note author`).
- On an email conversation a `Reply` is stamped with a Message-ID and channel
  `Email`; a note stays on chat.

Returns `MessageResponse` → **201**. A late first reply records a breach here.

### Change state

Body (`ChangeStateRequest`): `{ "state", "snoozedUntil"? }`.

- `Snoozed` without `snoozedUntil` → **422** (`Missing snooze time`).
- `snoozedUntil` in the past → **422** (`Domain rule violated`,
  "Snooze time must be in the future.").
- An unknown numeric state → **422** (`Unknown state`).

Returns `ConversationSummaryResponse` → **200**. A `Closed` state queues a
`conversation.closed` webhook and can record a resolution breach.

### Assignment

Body (`AssignConversationRequest`): `{ "teammateId"?, "teamId"? }`. Provide
exactly one, or neither to unassign.

- Both provided → **422** (`Ambiguous assignment`).
- A teammate or team not in this workspace → **422**.

Every call records an `AssignmentEvent` (`Manual`); assigning (not unassigning)
queues a `conversation.assigned` webhook. `AssignmentEventResponse`:
`{ id, conversationId, kind, actorTeammateId, fromTeammateId, fromTeamId,
toTeammateId, toTeamId, createdAt }`.

### Priority

Body (`SetPriorityRequest`): `{ "priority" }`. Re-stamps SLA targets from the
policy now governing the conversation, measured from creation — so escalating an
old conversation can put it immediately past due, recording a breach.

### `ConversationSummaryResponse` / `ConversationDetailResponse`

Summary: `{ id, workspaceId, inboxId, contactId, subject, state, snoozedUntil,
closedAt, assignedTeammateId, assignedTeamId, priority, channel,
firstResponseDueAt, firstRespondedAt, resolutionDueAt, firstResolvedAt,
slaPolicyId, tags[], createdAt, updatedAt, lastMessageAt }`. Detail adds
`messages[]` (`MessageResponse`, oldest first).

## Transfers and customer-context tools

`POST /api/conversations/{id}/transfer` accepts required nonempty
`destinationInboxId`. Both roles may move a scoped Chat conversation. Missing
parent gives **404**; Email channel or unavailable destination gives **422**.
Success returns the existing conversation's summary (**200**) with a new inbox
and recalculated SLA deadlines measured from original creation. Messages, tags,
state, assignment, channel, and first milestones remain. An actual move records
one history row; same-inbox repetition is a no-op. Move, history, and newly
detected breaches save together. `GET /api/conversations/{id}/transfer-events`
returns paged `{ id, conversationId, actorTeammateId, fromInboxId, toInboxId,
createdAt }`, oldest first then ID, after checking parent ownership.

`GET /api/contacts/{id}/attributes` returns `{ attributes, version }`.
`PATCH` at the same route accepts `{ changes: { key: "value", removedKey: null },
expectedVersion: "..." }`. Both roles may patch scoped contacts. Omitted keys
remain, strings set values, and null removes a key. Keys are trimmed with case
preserved; value whitespace is preserved. Maximums: 50 changes, 100-character
normalized keys, 2,000-character values, and 10,000-character final JSON. Blank or
colliding normalized keys and size violations give **400** without changes.
Empty changes are a no-op. Missing/foreign contact gives **404**; stale nonempty
version gives **409**. Success returns the saved map/token (**200**). Other contact
fields are untouched. Legacy full PUT still replaces the map and has no client
version precondition.

`GET /api/workspaces/{workspaceId}/contacts/duplicate-emails` returns paged
`{ normalizedEmail, contactCount }` groups of at least two nonblank trimmed/lowercase
emails, count descending then email. Totals count groups. Add `/members?email=...`
for paged `ContactResponse` members, name then ID. Email is required/nonblank,
maximum 320 characters. If fewer than two matches remain, members and total are
empty/zero. Both roles may read; queries are scoped and perform no merges. This
is the application's matching rule, not universal email identity equivalence.

`GET /api/contacts/{id}/support-summary` returns `{ totalConversations, open,
snoozed, closed, replies, notes, firstConversationAt, latestMessageAt,
latestConversationId }`. Both roles may read scoped contacts. Counts describe
current states and all message kinds, including opening replies. Latest orders
conversation message activity descending then ID; notes can be latest. Existing
empty history gives zero counts/null dates and ID; unknown contact gives **404**.
No paging or filters apply.

## Segment preview, cloning, and explanations

`POST /api/workspaces/{workspaceId}/segments/preview` accepts required `rules`
and `sampleLimit` (default 10, range 1-25). Both roles may preview. Requires 1-50
nonnull conditions with fields and a defined Match value. Malformed shape gives
**400**, invalid rule meaning **422**. Success (**200**) returns
`{ totalMatches, sample }` with contacts ordered by name then ID, no pagination
headers, and no persisted changes. Membership uses the existing SQL compiler.

`POST /api/segments/{id}/clone` is Admin-only and accepts required `name`, maximum
raw length 200, trimmed before saving. Returns **201** with a new SegmentResponse
and its normal GET Location. Source remains unchanged; rules are copied and
membership remains dynamic. Missing/foreign source gives **404**, unusable stored
rules **422**, exact existing workspace name **409**, invalid name **400**. Name
uniqueness retains existing case-sensitive semantics. Retries may encounter a
duplicate-name conflict after the first successful creation.

`GET /api/segments/{id}/contacts/{contactId}/explanation` allows both roles and
returns `{ segmentId, contactId, match, matches, conditions }`. Each condition is
`{ conditionIndex, field, operator, value, matches }`, with zero-based indexes in
original order. Both resources must belong to the caller's workspace (**404**
otherwise). Invalid stored rules or more than 50 conditions give **422**. The
diagnostic uses up to 51 sequential SQL membership queries, plus resource lookups,
within a read transaction. Configured comparison values are returned, not an
attribute dump. Existing segment semantics remain: Exists means nonnull, empty
strings exist, missing values satisfy negative text comparisons, and Before/After
exclude equality.

## Personal queues, bookmarks, drafts, and subject editing

All these routes require authentication. Personal queues, bookmarks, and drafts
belong to the caller's teammate ID as well as their workspace. Admins do not read
another teammate's private rows. Unknown or inaccessible IDs return **404**.

| Method | Route | Contract |
| --- | --- | --- |
| POST / GET | `/api/workspaces/{workspaceId}/saved-queues` | Create (**201**) / paged own list |
| GET / PUT / DELETE | `/api/saved-queues/{id}` | Read/update (**200**) / delete (**204**) |
| GET | `/api/saved-queues/{id}/conversations` | Execute current data with caller paging |
| PUT / DELETE | `/api/conversations/{id}/bookmark` | Set/clear own bookmark, repeated calls **204** |
| GET | `/api/workspaces/{workspaceId}/bookmarked-conversations` | Own summaries, newest bookmark then conversation ID |
| GET / PUT / DELETE | `/api/conversations/{id}/my-draft` | Private unsent text with version preconditions |
| GET / PUT | `/api/conversations/{id}/subject` | Shared subject editor with conversation version |

Saved-queue create/update accepts `{ "name": "Open", "filters": { "state": "Open" } }`.
Name is required, trimmed, maximum raw length 100. Filters support all queue
criteria and sort, exclude paging, and serialize to at most 10,000 characters.
Responses include ID, name, filters, schemaVersion=1, createdAt, updatedAt.
Duplicate names are allowed. Filters are validated on write and execution;
unsupported stored schemas or unavailable saved segments produce **422**, never
an unrestricted fallback. List is ordered by name then ID.

Draft GET returns `{ body, version, updatedAt }` or **404** when absent. PUT accepts
required nonblank `body` (maximum 20,000) and nullable `expectedVersion`. Null means
create-only (**201**); an existing draft needs its current nonempty token (**200**).
Conflicting creation or stale token gives **409**. DELETE requires the current
`expectedVersion` query parameter; missing/empty/invalid gives **400**, stale **409**,
absent **404**, success **204**. Saving a draft does not send a message or update
conversation activity. After 409, reload and compare text before deliberately retrying.

Subject GET returns `{ id, subject, version }`. PUT accepts nullable `subject`
(maximum raw length 500) and required nonempty `expectedVersion`. Outside spaces
are trimmed; blank clears to null. A real edit changes `UpdatedAt` and returns a
new version; a no-op keeps the version. The token covers the entire conversation,
so messages and priority changes can also make it stale (**409**). Subject edits
preserve message activity, assignments, state, and SLA targets.

## Batch triage and operational reads

`POST /api/workspaces/{workspaceId}/conversations/bulk-priority` accepts
`{ "conversationIds": ["..."], "priority": "Urgent" }`. Both roles may select
1-25 distinct nonempty IDs with a required defined priority. Invalid shape gives
**400**; any unavailable ID gives **422** with zero changes. Success returns
summaries ordered by ID (**200**). Priority, recalculated SLA targets, and breach
records commit together; a stale database write gives **409** and rolls back the
batch. Targets remain measured from creation time. Repeating the command uses
new server reads and can replace a later priority choice; it is not a client
version precondition.

`PUT /api/conversations/{id}/tags` accepts `{ "tagIds": [] }`, containing 0-20
distinct nonempty IDs. Both roles may replace the complete set. Empty clears
links; null/duplicates/oversize gives **400**. Parent missing/foreign gives **404**;
an unavailable tag gives **422** without changes. Success returns the conversation
summary with sorted tag names (**200**). Identified write conflicts give **409**.
Only links change, never the tag records. Later replacements may replace earlier
selections; this is not a stale-client merge.

`POST /api/workspaces/{workspaceId}/conversations/wake-due?limit=50` is Admin-only.
Limit must be 1-200 (**400** otherwise). It opens at most that many Snoozed
conversations whose nonnull snooze time is at or before the captured time, ordered
by snooze time then ID. Response is `{ conversationIds, evaluatedAt }` (**200**),
including an empty ID array when nothing is due. Repeated calls drain subsequent
batches. Assignment, SLA milestones, deadlines, and message activity are retained.
This command does not install an automatic worker.

`GET /api/conversations/{id}/timeline` returns a paged array of
`{ id, kind, occurredAt, message, assignment, slaBreach }` with exactly one source
payload present. Sources are Message=0, Assignment=1, SlaBreach=2. Order is occurred
time descending, source ascending, ID ascending. Message/assignment time is
CreatedAt; breach time is BreachedAt. Totals cover all sources, including internal
notes and the opening message. Both roles may read scoped conversations; missing
or foreign parent gives **404**. This feed does not imply history for subject/state
changes or inbox transfers.

`GET /api/workspaces/{workspaceId}/assignment-eligibility` is available to both
roles and returns paged `{ id, name, availability, assignedCount, capacityLimit,
eligible, reasons }` rows by creation time then ID. Reasons may contain `Away`,
`AtCapacity`, both, or neither. Count includes individually assigned Open/Snoozed
conversations; Closed and team-only assignments do not count. Null capacity is
unlimited. Includes the bootstrap Admin. Reads do not advance the inbox rotation
pointer and do not predict who will win the next assignment.

## SLA simulation and upcoming obligations

`POST /api/workspaces/{workspaceId}/sla/simulate` is Admin-only. Body requires
`inboxId`, a defined `priority`, and `createdAt`. Missing/malformed input gives
**400**; unavailable inbox or deadline arithmetic overflow gives **422**.
Returns **200** with `{ source, policyId, policyName, firstResponseMinutes,
resolutionMinutes, firstResponseDueAt, resolutionDueAt }`. Source is Policy,
InboxFallback, or None. Uses the real resolver and one selected policy; a missing
target on that policy does not borrow the inbox fallback. Due times start at the
supplied creation instant. No conversations, breaches, or deliveries are created.

`GET /api/workspaces/{workspaceId}/sla/upcoming` accepts all existing conversation
filters/paging and `withinMinutes` (default 60, range 1-10080). Both roles may read.
Returns paged `{ conversationId, subject, inboxId, kind, dueAt }` obligations with
`X-Evaluated-At` for the captured UTC time. Only non-Closed conversations with an
unresolved corresponding milestone and a due time in `[now, now+withinMinutes)`
qualify. Already-overdue targets and completed milestones are excluded. One
conversation can contribute two rows; totals count targets. Order is due time,
conversation ID, then kind. Invalid interval gives **400**; unknown segment stays
**422**. Reads change no deadlines or state.

## SLA policies & evaluation

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/sla-policies` | Admin | |
| GET | `/api/workspaces/{workspaceId}/sla-policies` | Any | Most specific first |
| GET | `/api/sla-policies/{id}` | Any | |
| PUT | `/api/sla-policies/{id}` | Admin | |
| DELETE | `/api/sla-policies/{id}` | Admin | |
| POST | `/api/workspaces/{workspaceId}/sla/evaluate` | Any | Sweep overdue; idempotent |

`POST`/`PUT` body: `{ "name", "inboxId"?, "priority"?, "firstResponseMinutes"?,
"resolutionMinutes"? }`.

- No target set → **422** (`SLA policy has no targets`).
- `inboxId` not in this workspace → **422** (`Unknown inbox`).
- A duplicate `(inboxId, priority)` scope → **409** (`Duplicate SLA policy scope`).

`SlaPolicyResponse`: `{ id, workspaceId, name, inboxId, priority,
firstResponseMinutes, resolutionMinutes, createdAt }`.
`SlaBreachEventResponse`: `{ id, conversationId, kind, dueAt, breachedAt,
slaPolicyId, createdAt }`.

## Reports

All under `/api/workspaces/{workspaceId}/reports/`, auth _Any_, sharing a
`ReportFilterRequest` query: `from`, `to` (bound conversation creation,
inclusive/exclusive), `inboxId`, `assignedTeammateId`, `assignedTeamId`,
`priority`, `tag`.

| Method | Route | Extra | Returns |
| --- | --- | --- | --- |
| GET | `reports/volume` | `?interval=Hour\|Day\|Week` (default `Day`) | `VolumeReportResponse` |
| GET | `reports/response-times` | | `ResponseTimeReportResponse` |
| GET | `reports/teammates` | | `BreakdownRowResponse[]` |
| GET | `reports/inboxes` | | `BreakdownRowResponse[]` |
| GET | `reports/tags` | | `TagDistributionResponse` |

Timings (`DurationStatsResponse`: `{ count, p50Minutes, p90Minutes, p95Minutes,
averageMinutes }`) are computed in memory over the materialized slice; null
percentiles mean an empty sample. A volume request whose range needs more than
1000 buckets → **422** (`Report interval too fine`).

## Webhooks

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/webhooks` | Admin | Returns secret once |
| GET | `/api/workspaces/{workspaceId}/webhooks` | Admin | |
| GET | `/api/webhooks/{id}` | Admin | |
| PUT | `/api/webhooks/{id}` | Admin | |
| DELETE | `/api/webhooks/{id}` | Admin | |
| GET | `/api/webhooks/{id}/deliveries` | Admin | Newest first |
| POST | `/api/workspaces/{workspaceId}/webhooks/dispatch` | Admin | Drain outbox |

`POST` body (`CreateWebhookRequest`): `{ "url", "events" }` — `url` must be a
valid URL and `events` non-empty, else **400**. Response
(`WebhookCreatedResponse`) includes `secret` (**shown only here**) → **201**.
`PUT` body adds `"isActive"`. `WebhookResponse` (and delivery payloads) never
include the secret.

`WebhookDeliveryResponse`: `{ id, subscriptionId, eventType, status,
attemptCount, lastAttemptAt, nextAttemptAt, responseStatusCode, error,
deliveredAt, createdAt, payload }`. Dispatch retries failures with backoff
(1/2/4/8 min) up to 5 attempts. Delivery requests carry `X-Harbor-Signature`
(`t=<unix>,v1=<hmac>`), `X-Harbor-Event`, `X-Harbor-Delivery`.

## Email channel

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/email/inbound` | Any | Ingest parsed mail |
| GET | `/api/messages/{id}/email` | Any | Render a reply as outbound email |

Inbound body (`InboundEmailRequest`): `{ "from", "fromName"?, "to", "subject"?,
"body", "messageId"?, "inReplyTo"?, "references"? }`. Routed to an inbox by `to`
(unknown address → **422**, `Unknown inbox address`); the sender is resolved to a
contact by email (created if new); `inReplyTo`/`references` thread onto an
existing conversation, else a new one starts through `ConversationStarter`.
Response (`InboundEmailResponse`): `{ conversationId, messageId, contactId,
startedNewConversation, createdContact }`.

`GET .../email` renders a teammate reply as the outbound email
(`RenderedEmailResponse`: `{ messageId, emailMessageId, from, to, subject,
inReplyTo, references[], body }`). A note, a chat-only inbox, or a contact's own
message → **422**.

## Help center (authoring)

Auth _Admin_ to write, _Any_ to read.

| Method | Route | Notes |
| --- | --- | --- |
| POST/GET | `/api/workspaces/{workspaceId}/collections` | |
| GET/PUT/DELETE | `/api/collections/{id}` | Delete with articles → **409** |
| POST/GET | `/api/workspaces/{workspaceId}/articles` | List `?status=`, `?collectionId=`, `?q=` |
| GET/PUT/DELETE | `/api/articles/{id}` | |
| POST | `/api/articles/{id}/publish` | |
| POST | `/api/articles/{id}/unpublish` | Returns to draft, keeps `publishedAt` |

Collections/articles derive a slug from the name/title when none is given;
duplicate slug in a workspace → **409**; an unusable slug → **422**; an unknown
collection on an article → **422**. Articles are created as **drafts**.
`CollectionResponse`: `{ id, workspaceId, name, slug, description,
publishedArticles, createdAt }`. `ArticleResponse`: `{ id, workspaceId,
collectionId, title, slug, body, status, publishedAt, createdAt, updatedAt }`.

## Public help center (anonymous)

| Method | Route |
| --- | --- |
| GET | `/api/public/workspaces/{workspaceId}/collections` |
| GET | `/api/public/workspaces/{workspaceId}/articles` (`?q=`, `?collectionId=`) |
| GET | `/api/public/workspaces/{workspaceId}/articles/{slug}` |

Published articles only; collections with nothing published are hidden. A draft's
slug returns **404** exactly like a slug that never existed, so these endpoints
cannot probe for unpublished work. `PublicArticleResponse`: `{ id, collectionId,
title, slug, body, publishedAt, updatedAt }`.

## Segments

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/segments` | Admin | |
| GET | `/api/workspaces/{workspaceId}/segments` | Any | |
| GET | `/api/segments/{id}` | Any | |
| PUT | `/api/segments/{id}` | Admin | |
| DELETE | `/api/segments/{id}` | Admin | |
| GET | `/api/segments/{id}/contacts` | Any | Contacts currently matching |

`POST`/`PUT` body: `{ "name", "rules" }` where `rules` is
`{ "match": "All"|"Any", "conditions": [ { "field", "operator", "value"? } ] }`.
Fields: `name`, `email`, `externalId`, `createdAt`, `lastSeenAt`,
`attributes.<key>`. Unusable rules → **422** (`Invalid segment rules`); a
duplicate name → **409**. `SegmentResponse`: `{ id, workspaceId, name, rules,
createdAt, updatedAt }`.

## Tags & canned replies

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| POST | `/api/workspaces/{workspaceId}/tags` | Admin | Names lowercased; duplicate → **409** |
| GET | `/api/workspaces/{workspaceId}/tags` | Any | |
| DELETE | `/api/tags/{id}` | Admin | |
| PUT | `/api/conversations/{conversationId}/tags/{tagId}` | Any | Idempotent; foreign tag → **422** |
| DELETE | `/api/conversations/{conversationId}/tags/{tagId}` | Any | |
| POST | `/api/workspaces/{workspaceId}/canned-replies` | Admin | Unique shortcut → **409** |
| GET | `/api/workspaces/{workspaceId}/canned-replies` | Any | `?q=` searches shortcut/title/body |
| GET | `/api/canned-replies/{id}` | Any | |
| PUT | `/api/canned-replies/{id}` | Admin | |
| DELETE | `/api/canned-replies/{id}` | Admin | |

`TagResponse`: `{ id, workspaceId, name, createdAt }`. `CannedReplyResponse`:
`{ id, workspaceId, shortcut, title, body, createdAt }`. Tagging/untagging a
conversation returns **204**.

Tag list accepts trimmed, case-insensitive substring `q` on name. Canned-reply
list also accepts `shortcut`, an exact case-insensitive match after trimming;
`refund` does not match `refund-late`. Blank values add no restriction. `q` and
`shortcut` combine with AND before count/paging. Search never rewrites stored names.
## Bootcamp additions: reports, editorial tools, templates, delivery operations

These routes follow the existing API-key and workspace scope rules. Resource-by-ID routes return 404 for unavailable foreign resources. Lists use the normal pagination headers. Implementation and executed verification are tracked in [the bootcamp](../astradocs/bootcamp/progress.md).

| Method and route | Permission | Behavior |
| --- | --- | --- |
| GET `/api/workspaces/{workspaceId}/reports/backlog-age` | Both roles | Report cohort filters; current Open/Snoozed counts and creation-age buckets `<24h`, `24h..<72h`, `>=72h`, evaluated at one captured UTC time. Empty cohort returns zeros. |
| GET `/api/workspaces/{workspaceId}/reports/team-workload` | Both roles | Paged team directory including zero-work teams, with roster counts and counts by direct AssignedTeamId. Work uses existing report filters. Shared roster membership does not multiply work. |
| GET `/api/workspaces/{workspaceId}/reports/volume.csv` | Both roles | Existing report filters and interval; same volume calculation as JSON. Fixed UTC/integer columns, UTF-8 without BOM, CRLF, attachment `conversation-volume.csv`. Header-only for empty data; 422 above 1,000 buckets. |
| POST `/api/articles/{id}/clone` | Admin | Body `{collectionId,title,slug?}`; new independent Draft with copied body, fresh ID, no publication date/history/votes. 201; unavailable collection 422; slug collision 409. |
| GET `/api/articles/{id}/revisions` | Admin | Paged before-change metadata, newest RecordedAt then ID. Full article bodies are omitted. |
| GET `/api/articles/{id}/revisions/{revisionId}` | Admin | One complete before-change snapshot, constrained to the owned parent article. |
| GET `/api/public/workspaces/{workspaceId}/articles/{slug}/related?limit=5` | Anonymous | Published source required; 1-10 results from at most 200 published same-collection candidates, excluding source. Ranking ties end with ID. |
| GET/PUT/DELETE `/api/articles/{id}/my-feedback` | Both roles | Own vote only. PUT requires `{helpful:true}` or false, replaces current answer, returns 200. GET absent 404; DELETE absent 204 after parent check. Concurrent first creation can return 409/reload. |
| GET `/api/articles/{id}/feedback-summary` | Admin | Total, Helpful, Unhelpful counts; zeros if no votes. Editing does not reset votes. |
| POST `/api/conversations/{id}/canned-replies/{replyId}/preview` | Both roles | No request body. Render current context without writes; returns Body and sorted distinct UsedTokens. |
| POST `/api/conversations/{id}/canned-replies/{replyId}/send` | Both roles | Render current context and append an authenticated teammate reply through the ordinary message/SLA/webhook workflow. 201 MessageResponse; repeated send creates another message. |
| POST `/api/workspaces/{workspaceId}/webhooks/dispatch?limit=50` | Admin | Limit 1-200; select active, Pending, due deliveries before ordering/taking the batch. Returns only attempted deliveries. |
| GET `/api/webhook-deliveries/{id}` | Admin | Scoped delivery including optional OriginalDeliveryId. |
| POST `/api/webhook-deliveries/{id}/redrive` | Admin | Failed original plus active subscription creates one fresh Pending child, copies exact payload, preserves original, returns 201. Repeats return the existing child with 200, even after completion/pause. Child redrive and nonfailed original without a child return 409. No HTTP send occurs here. |
| GET `/api/workspaces/{workspaceId}/webhooks/health` | Admin | Counts total/status, partitions Pending into Ready/Deferred/Paused, oldest ready NextAttemptAt, latest success DeliveredAt, and Failed/(Succeeded+Failed) rounded to four decimals or null with no terminal results. Excludes payloads/secrets/URLs/errors. |

The new backlog/CSV routes reject From >= To with 400. Creation-cohort date filters are inclusive From and exclusive To; volume resolution buckets may extend beyond those creation dates. Team workload retains existing report-filter behavior.

Canned-reply tokens are case-sensitive `contact.name`, `contact.email`, `teammate.name`, and `conversation.subject`, written inside double braces. Whitespace inside braces is trimmed; missing optional values become empty strings; substituted values are not reparsed. Unknown/malformed tokens, output above 20,000 characters, or blank output on send give 422. Preview can return blank output.

Article edits and actual publication-state changes save the previous state with the article update in one commit. Creation, no-op updates, and rejected changes create no revision. Revisions are deleted with their parent article. Public-content isolation uses the separate active publication snapshots implemented in S17; editorial history and approved public artifacts have different roles.

## Durable ingress, delivery attempts, and credentials

Senior implementation status and migration evidence remain in the [bootcamp tracker](../astradocs/bootcamp/progress.md). Apply the corresponding migrations before using these routes on an existing database.

Conversation creation optionally accepts one `Idempotency-Key` header containing 1-128 ASCII letters, digits, `-`, or `_`. Identity is scoped to workspace, authenticated teammate, and operation. Equivalent normalized requests replay the exact original 201 JSON/Location with `Idempotency-Replayed: true`; changed input under that identity returns 409. Invalid keys return 400. Only successful results are retained, for at least 24 hours and longer while the receipt remains; no cleanup is implemented. Unkeyed creation remains independent per call. The retained body is capped at 256 KiB. Authentication and workspace authorization run on every replay.

Inbound email with a nonblank MessageId now uses a receipt scoped to workspace, resolved inbox, and outer-trimmed case-sensitive MessageId. Matching canonical input replays the original 200 InboundEmailResponse without updating contacts or appending messages; conflicting input returns 409. Headers `Inbound-Receipt-Id` and `Inbound-Replayed` identify the result. GET `/api/inbound-email-receipts/{id}` returns scoped IDs, original creation flags, and ingest time, without message bodies. Blank/omitted MessageId keeps independent ingestion behavior. Known referenced threads must match the selected inbox and the actual contact's normalized sender address; mismatch returns 422 before writes. No historical receipts are fabricated for pre-feature messages.

These database command paths use immediate SQLite write transactions with at most three attempts for known busy/lock conflicts. Exhaustion returns 503 with `Retry-After: 1`. External HTTP is never performed inside those transactions.

Webhook dispatch now commits one 60-second lease and one Started attempt before each HTTP send. At most 50 reservations are processed per dispatch; an accepted older `limit` value above 50 is capped at 50. Only the current unexpired owner can save an outcome. AttemptCount counts reservations, including crashed/cancelled attempts; completed failures retain 1/2/4/8-minute backoff and the five-attempt maximum. Expired unfinished attempts become Unknown on recovery. Existing attempt counts are preserved without invented historical ledger rows. Leases do not guarantee exactly-once receiver effects.

Admin GET `/api/webhook-deliveries/{id}/attempts` returns normal paging, ordered by AttemptNumber ascending, with ID, number, StartedAt/FinishedAt, outcome Started/Succeeded/Rejected/TransportError/Unknown, optional response status, and a bounded failure code. It omits payloads, response bodies, URLs, secrets, and raw exceptions. Parent scope is checked first. Stop old dispatchers before switching to reservation accounting; old dispatchers ignore leases.

| Credential route | Request/result |
| --- | --- |
| POST `/api/teammates/{id}/api-keys` | `{name,lifetimeMinutes}`; nonblank trimmed name up to 100 characters, lifetime 15-43,200 minutes. 201 `{credential:{id,teammateId,name,createdAt,expiresAt,revokedAt},apiKey}`. Raw key appears only in this successful response. |
| GET `/api/teammates/{id}/api-keys` | Paged safe metadata for all retained keys, including expired/revoked ones; no hashes or raw values. |
| DELETE `/api/api-keys/{id}` | Revoke once; repeated revocation through another usable credential returns 204 and preserves the first timestamp. |

Owners manage their own credentials; Admins can manage teammates in the same workspace. Another owner's keys give 403 to an Agent; foreign resources give 404. At most five currently usable credentials per teammate are allowed, enforced transactionally; limit conflict returns 409. Expired/revoked keys do not consume usable slots. Authentication rejects expiry at `now >= ExpiresAt` and uses the teammate's current role. Revoking the calling key succeeds and its next request fails with 401. Initial/backfilled legacy credentials remain nonexpiring until revoked. Authentication reads the credential table only; the retained old hash column is not a fallback. A lost key-creation response cannot be recovered: revoke the unused credential and issue a replacement.

## Reviewed contact batches, routing, and notifications

| Route | Permission | Contract |
| --- | --- | --- |
| POST `/api/workspaces/{workspaceId}/contact-merges/preview` | Admin | `{sourceId,targetId}` creates a stored ten-minute plan; returns ID, expiry, field decisions, conflict values, reference counts, and fingerprint. Distinct owned contacts must share normalized nonblank email, have compatible external IDs, and fit 100 source conversations/1,000 source-authored messages. Ineligible requests return 422. |
| POST `/api/contact-merges/{id}/commit` | Admin in plan workspace | Returns the original result on repeat. First commit rechecks the fingerprint and eligibility, moves conversation/message-author references, applies target-wins fields, removes Source, and saves result atomically. Stale 409; expired uncommitted plan 410. Creator and committing Admin are recorded separately. |
| POST `/api/workspaces/{workspaceId}/contact-imports` | Admin | `{rows:[...]}`, 1-200 rows and 3 MiB request cap. Returns 201 Ready/Invalid batch with 1-based row errors, normalized valid input, and 24-hour expiry. No contacts are created during staging. |
| GET `/api/contact-imports/{id}` | Admin | Scoped immutable staged input/errors, status, and eventual contact-ID mappings. |
| POST `/api/contact-imports/{id}/commit` | Admin | Atomically create every row of a Ready batch and return saved mappings. Repeats return those same IDs. Invalid/unsupported 422; expired uncommitted 410. Duplicate emails/external IDs remain allowed; no upsert or partial commit. |
| GET `/api/inboxes/{id}/routing-rules` | Admin | Active immutable rule version and configuration token. Unconfigured owned inbox returns null tokens and an empty list. |
| PUT `/api/inboxes/{id}/routing-rules` | Admin | `{expectedVersion,rules}`; null version is create-only. Rules contain ID, name, TargetTeamId, Conditions as SegmentRuleSet, optional Priority/Channel. At most 20 rules, 1-10 conditions each, 100,000 serialized characters. Undefined enums/invalid shape 400; unusable conditions/teams 422; stale 409. Empty list disables routing. |
| POST `/api/inboxes/{id}/routing-rules/preview` | Admin | `{contactId,priority?,channel?}` returns chosen version/rule/team and skipped reasons without assignment or pointer mutation. |
| PUT/DELETE `/api/conversations/{id}/watch` | Both roles | Own idempotent watch/unwatch, 204. At most 200 watchers per conversation; full capacity 422. Parent scope is checked first. |
| GET `/api/workspaces/{workspaceId}/my-notifications?unreadOnly=true` | Both roles | Personal paged metadata, newest CreatedAt then ID. Contains conversation/message IDs and created/read times, no copied message body. |
| PUT `/api/notifications/{id}/read` | Owner | Sets first read time once; repeats 204. Missing/other owner/foreign ID gives 404. |

Merge field policy keeps target name/email, fills a missing target ExternalId from Source, unions attributes with target values winning, and keeps the latest LastSeenAt. Historical receipt/event IDs are retained. An unrecognized new live Contact foreign-key dependency blocks merging until its policy is implemented.

Routing is evaluated before auto-assignment for both new chat and new email conversations. First usable matching rule wins; deleted target teams are skipped. No match preserves existing fallback. AssignmentKind adds `Routing = 2`, preserving Manual/Auto values, and history adds optional RoutingVersionId/RoutingRuleId. Replacing configuration does not reassign existing work. New inbound contacts are inserted inside the outer transaction before SQL segment matching, so a later failure rolls their insertion back.

Notifications are generated only for new Contact/Reply messages appended to existing threads through chat/API or inbound email. Opening messages, notes, and teammate replies produce none. Notifications share the message commit. Recipients are the watcher set observed during staging; unwatch preserves old notification history. Admin role does not grant access to another person's personal notifications.
## Approved publications and scheduled drafts

Article authoring responses include `HasUnpublishedChanges`. Editing a Published article changes editorial content; public reads continue using its last active immutable snapshot. POST `/api/articles/{id}/publish` approves current content and switches its public slug/collection atomically. A live slug owned by another article gives 409. Unpublish removes the live route. Collection counts and public search/suggestions/related articles use approved snapshots. An active snapshot keeps its collection nonempty even when the editorial copy moves elsewhere.

Admin GET/PUT/DELETE `/api/articles/{id}/publication-schedule` reads, replaces, or cancels one durable schedule. PUT accepts `{ "expectedArticleVersion": "<current Guid>", "publishAt": "2026-10-01T12:00:00Z" }`. The article must be Draft; time must be strictly future and at most 90 days away. Stale version gives 409; invalid time/state gives 422. GET returns `articleVersion` and nullable `schedule`. Status is Pending, Completed, Cancelled, or NeedsReview, with a diagnostic reason and original intended time. Content edits invalidate pending approval; manual publish/unpublish cancels it.

The worker is disabled by default. Set `Workers:PublicationSchedules:Enabled=true` to scan every 30 seconds, processing at most 20 due schedules per pass. It rechecks article version, draft state, and current author Admin permission before using the same publication service as the manual endpoint. This is publication on a successful due scan, not an exact-second guarantee.
## UTC working calendars and reviewed SLA migration

Admin POST/GET `/api/workspaces/{workspaceId}/working-calendars` creates/lists immutable versions. POST body has `name` and `definition: { intervals: [{ day: "Monday", startMinute: 540, endMinute: 1020 }], closedDates: ["2026-12-25"] }`. Supply at least one nonempty weekly window, at most four nonoverlapping windows per weekday, and at most 100 closed UTC dates. Minutes are after UTC midnight; 1440 is allowed only as an end. The returned ID identifies that exact immutable version.

SLA policy create/update accepts optional `workingCalendarId`; foreign/unavailable references give 422. Null keeps elapsed-minute behavior. Simulation and conversation responses report the selected/applied calendar ID. Calculations retain fractional seconds and reject targets unreachable within 366 days. Existing conversations retain their deadlines when policy configuration changes.

Admin POST `/api/workspaces/{workspaceId}/sla/recalculations/preview` accepts `conversationIds` containing 1–100 distinct Open/Snoozed conversations and returns a stored five-minute plan. POST `/api/sla-recalculations/{id}/commit` applies the creating Admin's fresh plan, preserving priority and first milestones. Changed conversation/configuration inputs give 409; expired uncommitted plans give 410; unavailable selections give generic 422. Successful retries return the stored result. Commit detects new breaches at execution time and retains existing breach history.
## Claims, closure checklists, and inbox archival

Both roles may POST `/api/workspaces/{workspaceId}/conversations/claim-next` with `{}` or `{ "inboxId": "..." }`. The Available caller must be below their non-Closed workload capacity. Unavailable/full gives 409, an unavailable inbox gives 422, no candidate gives 204, and one committed claim gives a 200 summary. Candidate order is priority descending, CreatedAt ascending, ID; only Open work without team or teammate assignment qualifies. The endpoint has no response-loss idempotency contract.

Both roles may POST/GET `/api/conversations/{id}/checklist-items`. Create accepts `title` (trimmed 1–200 characters) and required `required` boolean, up to 50 items per thread. PUT `/api/checklist-items/{id}` accepts title, required, expectedVersion; PUT `/completion` accepts completed and expectedVersion; DELETE accepts a JSON expectedVersion body. Stale versions and mutation on Closed work give 409. Closing with unfinished required items gives 422 with `incompleteRequiredItemIds`. Optional items never block closure.

Admin PUT `/api/inboxes/{id}/archive` accepts required `archived` boolean and returns inbox metadata with nullable `archivedAt`. Repeated settings are no-ops. Default inbox listing includes active rows; `includeArchived=true` includes both. Existing detail/history remain available. New chat/email threads and transfers into archived destinations give 422. Correctly validated existing email replies and committed receipt replays remain allowed. Restoring clears ArchivedAt; existing email addresses remain reserved throughout archival.
## Chat attachments and private storage

POST `/api/conversations/{id}/attachments` accepts one multipart `file` for a scoped Chat conversation, at most 5 MiB. It returns 201 staged metadata; staging expires after 24 hours. AddMessageRequest accepts up to three distinct `attachmentIds`, all staged for that conversation by the caller. Invalid ownership/thread/state/expiry gives 422. Message reads include safe attached metadata. Email and opening-message attachments are outside this feature.

GET `/api/attachments/{id}/content` permits the staging uploader, or workspace teammates through an attached message's scoped conversation. Downloads use attachment disposition, a sanitized filename, and `application/octet-stream`. A missing blob produces 503 rather than an empty successful download. DELETE `/api/attachments/{id}` is uploader-only for staging; attached files give 409. Admin POST `/api/workspaces/{workspaceId}/attachments/reconcile` processes at most 100 expired/deleting/orphan artifacts. It never removes a referenced Attached blob.

Configure `Storage:PrivateRoot` as durable private storage shared by instances using the same database. The default is `.harbor-private` beneath the application content root. Filesystem paths and storage IDs are not returned by the API. Orphans and abandoned temporary files become eligible for reconciliation after 24 hours.
## Durable workspace event stream

Both roles may GET `/api/workspaces/{workspaceId}/events` with `text/event-stream` responses. Supported events: ConversationCreated, MessageCreated, StateChanged, AssignmentChanged. Data includes stable `eventId`, sequence, entity/conversation IDs, time, and bounded metadata; it excludes message bodies. Priority/tag changes are not covered. With no cursor, a `ready` frame starts at the current high-water mark. Send the prior SSE `id` as `Last-Event-ID` to resume later committed events.

SSE IDs are protected workspace-bound cursors with seven-day expiry. JSON `eventId` is the stable identity to deduplicate facts; resume tokens may refresh. Wrong-workspace/tampered/malformed cursors give 400; expired or purged history gives 410 and requires refreshing ordinary APIs. Heartbeats have no ID. Connections last at most five minutes, then reconnect and authenticate again. Processing is at least once across disconnects.

Admin POST `/api/workspaces/{workspaceId}/events/prune` removes at most 1,000 older-than-seven-days events from a sequence prefix and advances the watermark atomically. Global sequences can have gaps within one workspace. Persist/share Data Protection keys under the configured private root's `protection-keys` directory; losing the key ring invalidates previously issued cursors. Existing pre-feature writes have no backfilled stream history.
## Durable conversation exports

Admin POST `/api/workspaces/{workspaceId}/conversation-exports` accepts conversation filter fields directly as a JSON object. Paging and sort fields are rejected; output always orders CreatedAt then ID. Related IDs are validated and segment rules are captured at enqueue. Returns 202 with Location. GET `/api/conversation-exports/{id}` returns safe Queued/Running/Ready/Failed/Expired metadata. GET `/download` returns Ready JSON Lines, 409 when not ready, or 410 after expiry. Access is restricted to workspace Admins.

The first record is a version-1 manifest with processing snapshot time, frozen filter description, and row count. Remaining records are safe conversation summaries, without messages/bodies. Limits: 10,000 rows, 50 MiB, 250 rows per snapshot page, two-minute processing budget, five-minute worker lease, and at most two processing attempts. Ready artifacts expire after 24 hours. Data reflects the processing snapshot, not enqueue time. A stale worker cannot publish its output as Ready.

`Workers:ConversationExports:Enabled=true` enables a 30-second scan, one job per pass; default is disabled. Admin POST `/api/workspaces/{workspaceId}/conversation-exports/reconcile` processes at most 100 expired/orphan/temp artifacts. Use durable shared `Storage:PrivateRoot` storage. Snapshot reads can contend with writers depending on SQLite journal mode; the separate-connection test exercises WAL consistency explicitly.

## Configuration audit coverage

Admin GET `/api/workspaces/{workspaceId}/configuration-audit` supports actorId, entityId, entityKind, action, inclusive from, exclusive before, and normal paging. Entity kinds: TeammateAvailability, TeamMembership, CannedReply. Actions: Created, Updated, Deleted. Invalid enums or inverted time windows give 400. Results order occurredAt descending then ID.

Coverage is actual availability/capacity changes, membership addition/removal, and canned reply creation/update/deletion. No-ops add no event. Availability retains allowlisted before/after values; membership retains IDs and addition/removal; canned replies retain changed-field names but no title/shortcut/body content. Authenticated actor and target IDs remain historical even after a target is deleted. Audit and configuration mutations commit together. This does not audit every application change or reconstruct pre-feature history.
