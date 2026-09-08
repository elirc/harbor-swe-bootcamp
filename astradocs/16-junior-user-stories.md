# 25 junior user stories with step-by-step implementation plans

**Original planning briefs, now accompanied by implementation.** The source contains
the worked features. Use the [implementation bootcamp](bootcamp/README.md) and
[verification tracker](bootcamp/progress.md) for current behavior and test evidence.
The plans below preserve the original starting-point explanations so you can
compare a proposed change with its finished code or rehearse it on a practice branch.

The work is deliberately small: add a condition, normalize one value, update one
setting, or expose a small read endpoint. No story requires a new database table,
column, migration, external service, front end, scheduler, or concurrency protocol.
The last endpoint stories have more steps; split them across sessions.

## Choose one story

| Story | User-visible outcome | Practice | Suggested sessions |
| --- | --- | --- | --- |
| [US-01](#us-01) | Search contacts by external ID | Extend an existing search | 1–2 |
| [US-02](#us-02) | Filter contacts with/without email | Optional boolean filter | 1–2 |
| [US-03](#us-03) | Find contacts missing an external ID | Nullable fields | 1–2 |
| [US-04](#us-04) | Find contacts with/without conversations | Related-record query | 2 |
| [US-05](#us-05) | Save contact names without outer spaces | Small write normalization | 1–2 |
| [US-06](#us-06) | Rename an inbox | Small authorized update | 2–3 |
| [US-07](#us-07) | Search teammates by name/email | Search and safe responses | 1–2 |
| [US-08](#us-08) | Filter teammates by availability | Enum validation | 1–2 |
| [US-09](#us-09) | Filter teammates by role | Another enum filter | 1–2 |
| [US-10](#us-10) | Search teams by name | Preserve related data | 1–2 |
| [US-11](#us-11) | Search inboxes by name/address | Search a nullable field | 1–2 |
| [US-12](#us-12) | Filter inboxes by auto-assignment setting | True/false/omitted | 1–2 |
| [US-13](#us-13) | Turn inbox auto-assignment on/off | Required boolean input | 2–3 |
| [US-14](#us-14) | Search tags by name | Small list improvement | 1 |
| [US-15](#us-15) | Find a canned reply by exact shortcut | Exact versus partial match | 1–2 |
| [US-16](#us-16) | Find conversations without a subject | Shared query condition | 1–2 |
| [US-17](#us-17) | Find conversations awaiting first response | Business meaning of null | 2 |
| [US-18](#us-18) | Find conversations with/without tags | Collection membership | 1–2 |
| [US-19](#us-19) | Find conversations with an SLA target | Combine nullable fields | 1–2 |
| [US-20](#us-20) | Filter conversations by last update window | Date boundaries | 2–3 |
| [US-21](#us-21) | Put urgent conversations first | Predictable ordering | 2 |
| [US-22](#us-22) | Read a thread one page at a time | Small read endpoint | 2–3 |
| [US-23](#us-23) | Filter that message page by reply/note | Extend US-22 | 1–2 |
| [US-24](#us-24) | See contact email-coverage counts | SQL aggregation | 2–3 |
| [US-25](#us-25) | See a conversation's reply/note counts | Scoped aggregation | 2–3 |

A session can be 30–60 minutes, with additional time for builds. These estimates
are planning aids, not speed requirements. Start with US-01 or US-14 if you want
the smallest change. Only US-23 requires another story: complete US-22 first.

## A repeatable routine for every story

1. Read the story and explain the user benefit in your own words.
2. Open the named action and the nearest existing tests. Read only that path.
3. Write the expected IDs/counts for the suggested fixture before changing code.
4. Create the story's proposed test file under `tests/Harbor.Tests/Integration/`.
   Follow the class/constructor/namespace pattern in
   [ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs):
   derive from `ApiTestBase` and accept `HarborApiFactory` through the constructor.
5. Add a test for the new behavior. Run the story's command. Verify that at least
   one test was discovered and that a failure concerns the intended missing
   behavior. A compile error is a different problem to fix first.
6. Follow the numbered implementation steps, making one small change at a time.
7. Add the edge cases and rerun the focused command. Then run `dotnet test`
   before considering your completed implementation ready for review.
8. Update the relevant section of [the API reference](../docs/api-reference.md)
   with the finished behavior. Record the actual test result and inspect your diff.

If a test already passes before any change, check whether someone has implemented
the feature since this plan was written. Do not duplicate an existing endpoint.
This checkout contains other uncommitted work: inspect `git status --short` first
and undo only your own edits if you want to abandon an exercise.

## Shared details you can reuse without guessing

**Test setup.** [ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs)
provides `CreateWorkspaceAsync`, `CreateContactAsync`, `CreateInboxAsync`,
`CreateTeammateAsync`, `CreateTeamAsync`, `StartConversationAsync`,
`CreateTagAsync`, `TagConversationAsync`, and `ActAs`/`ActAsAdminOf`.
Creating a workspace automatically creates its bootstrap admin and switches the
client to that workspace's key. Remember this extra teammate in role/availability
counts. Creating another workspace also switches the active key: switch back
before asserting the first workspace's results.

The contact helper does not accept an external ID. For that fixture, send a
`CreateContactRequest` with `Client.PostAsJsonAsync` and the existing `Json`
serializer options. The inbox helper does not accept an email address; use
`CreateInboxRequest` directly when an address matters. For exact timestamps,
use `Factory.WithDb`, edit only the test's rows, and save. Do not use sleeps.

**New filter records.** Several stories share one controller. If the proposed
`ContactFilterRequest`, `TeammateFilterRequest`, `InboxFilterRequest`, or
`MessageFilterRequest` already exists from another story, extend it. Otherwise
create it in [Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), inheriting
`PageRequest`. Preserve existing query-string names such as `q`, `page`, and
`pageSize`. Bind the whole object with `[FromQuery]`; name the parameter `filter`,
not `page`. Forward that object to `ToPageAsync` so inherited paging still binds.

**Boolean meaning for these new filters.** `true` selects rows satisfying the
condition, `false` selects its opposite, omission applies no restriction, and
invalid text such as `banana` produces 400. Use nullable booleans to represent
all three meaningful inputs. Do not change the older `unassigned` or `slaBreached`
contracts, whose existing `false` means no restriction.

**Text presence.** In this document, text is present only if it is non-null and
contains a non-whitespace character. Null, empty, and whitespace-only are absent.
Express this in the database query with a null guard and trimmed-text comparison;
do not load every entity and then filter a list. Use direct fixture arrangement
for legacy empty/whitespace values if request validation prevents creating them.

**Access and page checks.** Read stories permit an Admin or Agent in the same
workspace. Missing keys give 401; another workspace's route gives 403. On a
by-ID route, a foreign resource is hidden like a missing one with 404. Keep
workspace predicates in data queries, including aggregates and subqueries. For
lists, assert both the returned IDs and `X-Total-Count`, and check a second page.
New predicates belong before ordering, projection, and `ToPageAsync`.

**No hidden schema work.** All plans reuse existing persisted properties. New
filter/request/response types are API shapes; they are not new database entities.
If you think a migration is necessary, pause and reread the bounded scope.

<a id="us-01"></a>
## US-01: Search contacts by external ID

**User story.** As a support agent, I want the existing contact search to find a
customer's external reference, so I can locate them using an ID from our other system.

**Current gap and scope.** `ContactsController.List` searches name and email.
Extend that existing `q` search to `ExternalId`; do not add a second search route
or change how contact records are written. Dependency: none.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs),
[Contact](../src/Harbor.Domain/Entities/Contact.cs), and
[ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs).
Proposed new test file: `US01ContactExternalSearchTests.cs`.

**Acceptance criteria.** `q=crm-42` matches an external ID of `CRM-420`, even when
name/email do not contain that text. Name and email matches still work. Null
external IDs do not cause errors. Blank/omitted `q` applies no restriction. Foreign
contacts never appear. The response remains the existing paged contact array.

**Implementation plan.**

1. Locate the existing `needle` and `Where` expression in `List`.
2. Create Alice with external ID `CRM-420` and unrelated name/email text; create
   Bob with no external ID. Use the direct request method described above.
3. Write a test searching `crm-42` and expecting only Alice's ID. It should fail
   before the change because the existing fields do not match.
4. Add a third OR branch to the existing search condition. Guard `ExternalId`
   against null, lowercase it as the other branches do, and use substring matching.
5. Preserve the outer workspace restriction. Do not accidentally OR the new
   branch with the entire tenant predicate.
6. Preserve current normalization of `q`, the response mapping, and pagination.
   When a contact filter record already exists, use its `Q` rather than adding
   another competing query parameter.
7. Add tests for an email-only match, a name-only match, null external ID, no
   match, and the same reference in another workspace.
8. Document that `q` searches three fields with OR semantics.

**Concrete check.** Alice=`CRM-420`, Bob=null, and another tenant's Eve=`CRM-420`:
the own-workspace request returns Alice only, with total 1. Add a second own
matching reference and request page size 1; total must be 2 while body length is 1.

**Run:** `dotnet test --filter "FullyQualifiedName~US01"`.

**Common mistake:** requiring name, email, and external ID to all match by using
AND. These are alternative ways to find one contact.

**Done when:** the new reference-only test and old search behaviors pass, docs
name the added field, and you can explain why the null guard is needed.

<a id="us-02"></a>
## US-02: Filter contacts with or without an email address

**User story.** As an agent, I want to find contacts missing email addresses, so
I can see which customers need another contact method.

**Current gap and scope.** The contact list has text search but no email-presence
filter. Add `hasEmail` to the existing list; do not validate, repair, or send mail
to stored addresses. Dependency: none.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[Paging.cs](../src/Harbor.Api/Infrastructure/Paging.cs).
Proposed test file: `US02ContactEmailFilterTests.cs`.

**Acceptance criteria.** `hasEmail=true` includes present email text;
`hasEmail=false` includes absent text; omission includes both. Existing `q`
combines with this filter using AND. Invalid boolean text returns a 400 problem.
Paging totals describe the filtered set; existing roles and tenant boundaries stay intact.

**Implementation plan.**

1. Read the shared boolean and text-presence definitions above.
2. Create/reuse `ContactFilterRequest : PageRequest`. Preserve `Q` and add a
   nullable boolean `HasEmail` property.
3. Update `List` to bind the filter record. Move references from the old `q` and
   `paging` parameters to `filter.Q` and the inherited paging properties.
4. Arrange one contact with `alice@example.test` and one with null email. Add
   empty and spaces-only cases through `Factory.WithDb` if necessary.
5. Write separate true, false, and omitted-filter tests before adding the predicate.
6. In the query, apply the presence predicate only when `HasEmail` has a value;
   use its logical opposite for false. Keep all filtering before `ToPageAsync`.
7. Add a mixed-case name search combined with `hasEmail=false` to establish AND
   behavior, and an invalid `hasEmail=banana` request expecting 400.
8. Verify `page=2&pageSize=1` still binds after replacing the action parameters.
9. Document true/false/omitted explicitly rather than only showing a true example.

**Concrete check.** With Alice=email, Bob=null, Cara=empty, Dee=spaces: true
returns Alice; false returns Bob/Cara/Dee; omission returns all four. A search
for `Bob` combined with false returns Bob only. Use returned IDs, not assumed row order.

**Run:** `dotnet test --filter "FullyQualifiedName~US02"`.

**Common mistake:** making `HasEmail` a non-nullable `bool`, which makes omission
look like false and silently changes the default list.

**Done when:** all three inputs, malformed input, combined search, tenant scope,
and second-page metadata are checked, and existing contact tests still pass.

<a id="us-03"></a>
## US-03: Find contacts missing an external reference

**User story.** As an agent, I want to list contacts with or without an external
ID, so I can identify records that have not been linked to our customer system.

**Current gap and scope.** `ExternalId` already exists but cannot be filtered by
presence. Add `hasExternalId`; do not generate IDs or enforce uniqueness. Dependency:
none; this can reuse US-02's filter record if that story is complete.

**Open first:** [Contact](../src/Harbor.Domain/Entities/Contact.cs),
[ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs), and
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs).
Proposed test file: `US03ContactExternalIdFilterTests.cs`.

**Acceptance criteria.** `true` means non-null, nonblank external text; `false`
means absent text; omission has no effect. Values are inspected, not rewritten.
Search and any already implemented contact filters combine using AND. A foreign
tenant's unlinked contacts must not influence rows or totals.

**Implementation plan.**

1. Create/reuse `ContactFilterRequest`, preserving `Q` and paging plus any filters
   previously added. Add nullable `HasExternalId`.
2. If this is the first contact-record filter, follow US-02's parameter-binding
   steps without implementing its `HasEmail` feature as an extra requirement.
3. Arrange four own contacts with external values `CRM-7`, null, empty, and spaces.
   Create an unrelated contact in another workspace and switch back afterward.
4. Write a true-filter test expecting only the linked own contact.
5. Add the null/trimmed-text presence predicate inside `List` when the option is
   supplied. For false, choose its complement; do not only compare to null.
6. Add omitted and false tests, plus `hasExternalId=banana` returning 400.
7. Add a `q` combination that excludes one otherwise matching contact. If US-01
   is absent, use a name match so this story remains independent.
8. Fetch the linked contact afterward and confirm its external ID is unchanged.
   This prevents accidentally turning a read into normalization work.
9. Check paging totals and document the meaning of “has.”

**Concrete check.** The four own contacts above produce true total 1 and false
total 3; the foreign contact contributes to neither total. Omitting the filter
returns the four own contacts. Empty and whitespace count as absent on purpose.

**Run:** `dotnet test --filter "FullyQualifiedName~US03"`.

**Common mistake:** adding an index or migration just because the API gained a
filter property. `ExternalId` is already persisted.

**Done when:** presence semantics, combined filtering, invalid input, unchanged
stored values, and tenant isolation are demonstrated by the tests.

<a id="us-04"></a>
## US-04: Filter contacts by whether they have conversations

**User story.** As an agent, I want to distinguish contacts who have contacted
support from those with no conversations, so I can investigate the right group.

**Current gap and scope.** Add `hasConversations` to contact listing. A conversation
in any state counts. This is a read filter, not a cleanup/delete operation.
Dependency: none; reuse the contact filter record if it already exists.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs)
(`List` and the existing `Delete` existence check),
[HarborDbContext](../src/Harbor.Infrastructure/HarborDbContext.cs), and
[ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs).
Proposed test file: `US04ContactConversationFilterTests.cs`.

**Acceptance criteria.** True selects contacts having at least one conversation;
false selects none; omission is unrestricted. Open, Snoozed, and Closed all count.
One contact with three conversations appears once. Search, paging, and tenant
isolation remain correct; malformed boolean input produces 400.

**Implementation plan.**

1. Add nullable `HasConversations` to `ContactFilterRequest`; use the shared
   binding instructions if introducing the record for the first time.
2. Arrange Alice with two conversations, Bob with one closed conversation, and
   Cara with none. Use the state endpoint to close Bob's thread and assert success.
3. Write a true-filter test expecting Alice and Bob exactly once each.
4. Add a correlated `db.Conversations.Any(...)` condition to the contact query.
   Match the contact ID and requested workspace inside that existence check.
5. Negate that existence condition for false. Keep the query deferred until the
   normal paging call; do not loop over contacts issuing one query per person.
6. Write the false test expecting Cara and the omission test expecting all three.
7. Create matching data in a second tenant, switch back, and prove it cannot
   change either result set or the total header.
8. Combine with a name search and page size one. Confirm duplicate conversations
   do not inflate the contact count.
9. Document explicitly that closed conversations still establish support history.

**Concrete check.** Two threads for Alice and one for Bob mean two matching
contacts, not three. Cara is the only false match. A join without deduplication
could return Alice twice; an existence query expresses the intended question directly.

**Run:** `dotnet test --filter "FullyQualifiedName~US04"`.

**Common mistake:** querying only Open conversations because the word “has” was
mistaken for “currently working on.” This story is about any stored history.

**Done when:** multi-conversation contacts appear once, closed history counts,
and you can explain the difference between counting contacts and conversations.

<a id="us-05"></a>
## US-05: Remove accidental outer spaces from contact names

**User story.** As an agent, I want saved contact names to exclude accidental
leading/trailing spaces, so records display consistently after creation and edits.

**Current gap and scope.** `Create` and `Update` currently assign `request.Name`
directly. Trim its outer whitespace in both paths. Preserve capitalization,
accents, and internal spacing. Do not backfill existing rows or change email,
external ID, or attributes behavior. Dependency: none.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs),
[contact request records](../src/Harbor.Api/Contracts/Requests.cs), and
[ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs).
Proposed test file: `US05ContactNameNormalizationTests.cs`.

**Acceptance criteria.** Creating `"  Maya Chen  "` saves/returns `"Maya Chen"`;
updating to `" Ada Lovelace "` saves/returns `"Ada Lovelace"`. `"Mary  Ann"`
keeps its two interior spaces. Null, empty, whitespace-only, and over-200-character
raw input remain 400 through the existing validation contract. Permissions do not change.

**Implementation plan.**

1. Inspect `Required` and `MaxLength(200)` on both request records. Keep their
   validation on raw input; this story does not expand the accepted input length.
2. Add a creation test submitting outer spaces, then fetch the created contact
   with GET and assert the stored normalized name as well as the creation body.
3. Change the name assignment in `Create` to use the trimmed validated input.
   Keep this local; a generic text-normalization service is unnecessary.
4. Add an update test with a different padded name, and apply the same trim to
   the `Update` assignment.
5. Add a test preserving interior spaces and casing. Use a non-ASCII name too
   to confirm you have not replaced characters or lowercased names.
6. Send empty, spaces-only, missing/null, and too-long values; assert 400 and
   verify an invalid update did not modify the existing contact.
7. Confirm other update fields still persist correctly by running the existing
   contact endpoint tests. Do not replace the request with a name-only endpoint.
8. Document outer-whitespace trimming for creation and update, including the
   unchanged raw length limit.

**Concrete check.** Store `"  Mary  Ann  "` as `"Mary  Ann"`. A 201-character
raw value is still invalid even if removing spaces would shorten it below 201.
That choice keeps validation behavior predictable and avoids silently changing scope.

**Run:** `dotnet test --filter "FullyQualifiedName~US05|FullyQualifiedName~ContactEndpointsTests"`.

**Common mistake:** normalizing only the response, so a later GET returns the
original padded database value.

**Done when:** create and update round-trips persist the normalized value,
invalid updates preserve old data, and internal spelling/spacing remain intact.

<a id="us-06"></a>
## US-06: Rename an inbox without changing its settings

**User story.** As an administrator, I want to rename a support inbox, so its
label stays understandable when our team's responsibilities change.

**Current gap and scope.** Inboxes have create/list/detail operations but no
name-update action. Propose `PUT /api/workspaces/{workspaceId}/inboxes/{id}/name`
with body `{ "name": "Billing support" }`. Only Name changes. Dependency: none.

**Open first:** [InboxesController](../src/Harbor.Api/Controllers/InboxesController.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs),
[Inbox](../src/Harbor.Domain/Entities/Inbox.cs), and
[WorkspaceEndpointsTests](../tests/Harbor.Tests/Integration/WorkspaceEndpointsTests.cs).
Proposed test file: `US06RenameInboxTests.cs`.

**Acceptance criteria.** An own-workspace Admin receives 200 and the existing
`InboxResponse` with a trimmed name. A subsequent GET returns that name. Invalid
name input gives 400. Agents get 403 and missing keys 401. A foreign workspace
route is 403; a missing/foreign inbox ID within the caller's own route is 404.
Email, SLA minutes, auto-assignment flag, rotation pointer, and CreatedAt are preserved.

**Implementation plan.**

1. Add a proposed `RenameInboxRequest` record with a required Name and the same
   raw maximum length of 200 used by inbox creation.
2. Add a `Rename` action to `InboxesController`. Its class already supplies the
   workspace/inboxes prefix, so the method route should append `{id:guid}/name`.
3. Mark this action Admin-only using the pattern on the existing `Create` action.
4. Load the inbox by both `id` and `workspaceId`, as in `GetById`. Return 404
   when the scoped lookup finds nothing; do not fetch by ID alone.
5. Assign only the trimmed Name. Call `SaveChangesAsync` once, then return the
   inbox's existing response mapping with 200.
6. In the test, create an inbox with non-default settings and remember them.
   Send the rename, GET it, and compare every pre-existing setting afterward.
7. Inspect the rotation pointer with `Factory.WithDb` before/after because it
   is not part of `InboxResponse`. Do not reset it while changing a label.
8. Exercise the permissions and invalid-input cases above. For a failed request,
   GET as Admin afterward and establish the original name remains.
9. Document the route, Admin requirement, trimmed name, and response shape.

**Concrete check.** `Support` renamed using `" Billing "` becomes `Billing`;
auto-assignment can remain true and the original email address remains identical.
Renaming to the existing name again returns 200. Duplicate inbox names remain
allowed, as creation does not establish a name-uniqueness rule.

**Run:** `dotnet test --filter "FullyQualifiedName~US06"`.

**Common mistake:** reusing `CreateInboxRequest` and overwriting omitted settings
with defaults. A name-only request makes the intended write smaller and clearer.

**Done when:** round-trip persistence, untouched settings, validation, and the
role/workspace matrix are checked. Do not add an inbox rename webhook in this story.

<a id="us-07"></a>
## US-07: Search teammates by name or email

**User story.** As an agent, I want to search the teammate directory by name or
email, so I can find the right colleague without paging through everyone.

**Current gap and scope.** `TeammatesController.List` currently returns the
workspace directory without search. Add optional `q` while preserving the
existing safe response shape and page headers. Dependency: none.

**Open first:** [TeammatesController](../src/Harbor.Api/Controllers/TeammatesController.cs),
[ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs) for a
search example, and [TeamAndTeammateEndpointsTests](../tests/Harbor.Tests/Integration/TeamAndTeammateEndpointsTests.cs).
Proposed test file: `US07TeammateSearchTests.cs`.

**Acceptance criteria.** Search is a case-insensitive substring across Name OR
Email; outer query spaces are ignored. Blank/omitted `q` leaves the list unchanged.
Results remain name-ordered with an ID tie-breaker, scoped, and paginated. The
list must not start exposing API keys or key hashes.

**Implementation plan.**

1. Create/reuse `TeammateFilterRequest : PageRequest` with optional `Q`. Preserve
   fields from other teammate stories if they already exist.
2. Bind it in `List`. Start with the existing workspace-scoped teammate query.
3. Arrange a teammate whose name includes Maya, another whose email contains
   `billing-lead`, and one unrelated teammate. Account for the bootstrap admin.
4. Write separate name-only and email-only tests, expecting explicit teammate IDs.
5. For nonblank Q, trim/lowercase the search value and add an OR predicate over
   the two columns before projection or pagination. Follow the contact-search pattern.
6. Keep name ordering and append `ThenBy` on ID to make equal-name pages stable.
   Continue mapping to `TeammateResponse`, not the key-bearing creation response.
7. Add mixed-case, padded, empty, no-match, and foreign-workspace fixtures.
8. Check a page size of one with multiple matches and inspect JSON to confirm
   it has no `apiKey` or `apiKeyHash` fields.
9. Document the searchable fields and unchanged default listing behavior.

**Concrete check.** `q=BILLING-LEAD` can match `billing-lead@example.test` even
when that teammate's name has no matching text. The bootstrap admin must not
accidentally be omitted from an unfiltered directory assertion.

**Run:** `dotnet test --filter "FullyQualifiedName~US07"`.

**Common mistake:** selecting the entire entity or the creation response because
it is convenient. Read responses intentionally omit credentials.

**Done when:** both search fields work independently, query omission preserves
all own teammates, and page metadata and safe response fields are tested.

<a id="us-08"></a>
## US-08: Filter teammates by availability

**User story.** As an agent, I want to show Available or Away teammates, so I can
choose whom to contact based on their recorded status.

**Current gap and scope.** Availability is already stored and can be updated.
Add the read filter `availability=Available|Away`; do not change assignment or
status-update behavior. Dependency: none; US-07's filter record can be reused.

**Open first:** [TeammateAvailability](../src/Harbor.Domain/TeammateAvailability.cs),
[TeammatesController](../src/Harbor.Api/Controllers/TeammatesController.cs), and
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs) for existing enum validation.
Proposed test file: `US08TeammateAvailabilityFilterTests.cs`.

**Acceptance criteria.** An omitted value means every availability; a supplied
value means exact equality. Both defined states work. Unknown text and undefined
numeric values such as 999 produce 400 validation problems. Search, if present,
combines using AND. Available does not claim the teammate has spare assignment capacity.

**Implementation plan.**

1. Create/reuse `TeammateFilterRequest`, retaining Q and paging. Add nullable
   `Availability` of the existing domain enum type.
2. Add enum validation following `ConversationFilterRequest.Sort` so numeric 999
   cannot silently become an undefined enum value. Valid omission must still pass.
3. Bind the filter record in `List` if not already done. Apply equality on
   `t.Availability` only when the nullable option has a value.
4. Arrange one Available and one Away teammate using `SetAvailabilityAsync`.
   Explicitly set the bootstrap admin's availability too, or include its default
   state in every expected result; do not forget that row.
5. Write true state-selection tests expecting exact IDs for each enum value.
6. Add no-filter, unknown-text, and `availability=999` requests. Assert problem
   status/media type for the invalid inputs rather than only an empty array.
7. Include an Available teammate whose capacity is full and confirm it still
   appears. Capacity-aware assignment eligibility is outside this filter's meaning.
8. Test paging and another tenant's Available row; document the distinction
   between status and assignability.

**Concrete check.** If Ada is Available and Bob plus the bootstrap admin are
Away, Available yields Ada alone and Away yields the other two. Capacity does
not change these sets. Omission includes all three.

**Run:** `dotnet test --filter "FullyQualifiedName~US08"`.

**Common mistake:** calling `AutoAssigner` to answer a directory query. That
helper selects a person and advances a rotation pointer; reading a filter must not do that.

**Done when:** enum validation, exact status selection, unchanged default results,
and lack of assignment side effects are all established.

<a id="us-09"></a>
## US-09: Filter teammates by role

**User story.** As an agent, I want to find the administrators in my workspace,
so I know whom to ask for a configuration change.

**Current gap and scope.** Add `role=Admin|Agent` to the existing teammate list.
This reads a stored role; it does not grant roles, edit permissions, or restrict
the listing to administrators. Dependency: none; reuse the teammate filter record.

**Open first:** [TeammateRole](../src/Harbor.Domain/TeammateRole.cs),
[TeammatesController](../src/Harbor.Api/Controllers/TeammatesController.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs).
Proposed test file: `US09TeammateRoleFilterTests.cs`.

**Acceptance criteria.** Defined role values filter exactly; omission selects all
roles. Invalid text/undefined numeric values return 400. Own-workspace Agents
can list Admins. Existing name/email search and availability filters, when
implemented, narrow the same query. No credential fields appear in the response.

**Implementation plan.**

1. Add nullable `Role` to the proposed/reused `TeammateFilterRequest` with enum
   validation based on the existing `TeammateRole` type.
2. If the action still takes only a `PageRequest`, change it to this record and
   preserve inherited paging. Do not replace it again if US-07/08 already did so.
3. Arrange two Agent teammates. Use the workspace's existing bootstrap Admin
   as the positive Admin fixture instead of accidentally assuming there is none.
4. Write a `role=Admin` test expecting that bootstrap Admin's ID, then one for
   `role=Agent` expecting exactly the two created Agents.
5. Add one equality predicate on Role before ordering/projection/pagination.
   Do not add an Admin-only authorization attribute to this read endpoint.
6. Switch the client to a created Agent's key and repeat the Admin listing.
   This checks the user's actual need to discover whom to ask.
7. Add omission, malformed role, another-workspace, and page-size-one cases.
   If availability filtering exists, add one combined-filter case as well.
8. Read the JSON shape and retain the existing safe mapping. Document that the
   filter changes which teammates are returned, not who the caller becomes.

**Concrete check.** Two newly created Agents plus the bootstrap Admin means
three own teammates. Admin total is one; Agent total is two; omission total is
three. Your authenticated key does not change role when you change the query string.

**Run:** `dotnet test --filter "FullyQualifiedName~US09"`.

**Common mistake:** confusing a filter named Role with authorization. The caller's
permissions come from their authenticated identity, not this request parameter.

**Done when:** an Agent can discover own Admins without seeing another tenant
or sensitive credentials, and role validation is covered.

<a id="us-10"></a>
## US-10: Search teams by name while preserving their rosters

**User story.** As an agent, I want to search teams by name, so I can find the
right group for an escalation and still see its members.

**Current gap and scope.** `TeamsController.List` already includes memberships
but has no text query. Add `q` for team Name only; do not search teammate names
or change membership operations. Dependency: none.

**Open first:** [TeamsController](../src/Harbor.Api/Controllers/TeamsController.cs),
[Responses.cs](../src/Harbor.Api/Contracts/Responses.cs) for `TeamResponse`, and
[TeamAndTeammateEndpointsTests](../tests/Harbor.Tests/Integration/TeamAndTeammateEndpointsTests.cs).
Proposed test file: `US10TeamSearchTests.cs`.

**Acceptance criteria.** Case-insensitive substring search trims the query;
omitted/blank Q returns the previous list. Matching teams retain their member
IDs. No matching name gives an empty array/zero total. Results remain name-ordered,
with ID breaking ties, and obey existing tenant and role rules.

**Implementation plan.**

1. Locate `List` and identify the existing `Include(t => t.Members)`. Keep that
   relationship loading because `TeamResponse` needs member IDs.
2. Add an optional `[FromQuery] string? q` parameter alongside the existing
   `[FromQuery] PageRequest paging`; this small story does not need a new record.
3. Introduce a local workspace-scoped `query` before the existing execution chain.
4. Arrange Billing Support and Billing Escalations, plus an unrelated Sales team.
   Add a known teammate to one Billing team using the existing member endpoint.
5. Write a `q=billing` test expecting both Billing IDs and the known member ID
   in the matching team's response.
6. For a nonblank query, trim/lowercase the value and filter team Name before
   Include/order/paging. Keep the existing response mapping after materialization.
7. Append an ID tie-breaker after name ordering, without removing alphabetical order.
8. Add padded/case-varied, no-match, omission, foreign-tenant, and second-page tests.
   Compare member IDs as a set unless their order is part of a separate contract.
9. Document that the query searches team names, not members or emails.

**Concrete check.** `q=BILLING&pageSize=1` returns one Billing team with total 2.
Walking both pages finds the two Billing IDs exactly once, and roster data is
still present for the team that has a member.

**Run:** `dotnet test --filter "FullyQualifiedName~US10"`.

**Common mistake:** dropping `Include(Members)` while simplifying the query and
returning empty rosters. Search should narrow teams, not erase their relationships.

**Done when:** name search, complete roster responses, paging totals, and default
listing behavior are all protected by focused tests.

<a id="us-11"></a>
## US-11: Search inboxes by name or inbound email address

**User story.** As an agent, I want to search inboxes using their label or inbound
address, so I can identify the correct channel for a customer's issue.

**Current gap and scope.** The inbox list has no search input. Add `q` matching
Name OR EmailAddress. Do not change address normalization, email routing, or the
stored inbox configuration. Dependency: none.

**Open first:** [InboxesController](../src/Harbor.Api/Controllers/InboxesController.cs),
[Inbox](../src/Harbor.Domain/Entities/Inbox.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[WorkspaceEndpointsTests](../tests/Harbor.Tests/Integration/WorkspaceEndpointsTests.cs).
Proposed test file: `US11InboxSearchTests.cs`.

**Acceptance criteria.** Case-insensitive substring matching works independently
on name and address. A null address is safe. Blank/omitted Q returns all own
inboxes. Creation-time ordering stays in place, with ID as a tie-breaker. Arrays,
page headers, permissions, and stored settings retain their existing meaning.

**Implementation plan.**

1. Create/reuse `InboxFilterRequest : PageRequest` with optional Q. Preserve an
   AutoAssign filter if US-12 was implemented first.
2. Bind this record in `List` and retain the existing workspace existence check.
3. Move the workspace-scoped inbox query into a variable so optional conditions
   can be added before the current ordering/projection/paging chain.
4. Arrange Billing with no email, General with `invoices@example.test`, and
   Sales with an unrelated address. Use direct `CreateInboxRequest` calls for
   addresses because the shared inbox helper does not accept that parameter.
5. Write one name-only test and one address-only test with explicit expected IDs.
6. Add the two OR branches using a normalized query value and a null guard for
   EmailAddress. Use query-time matching; do not assign new values to the entities.
7. Keep ordering by CreatedAt and append ID. Map to `InboxResponse` and pass
   the inherited paging options through to `ToPageAsync`.
8. Add null-address, empty query, no-match, foreign workspace, and page-boundary
   cases. Fetch an inbox after searching and confirm its settings are unchanged.
9. Document searchable fields and that the matching address is the inbox's
   inbound routing address, not a contact's email.

**Concrete check.** `q=INVOICES` returns General through its address even though
its name does not match. `q=billing` returns the name match safely despite null
email. A matching address in another tenant must not add to the total.

**Run:** `dotnet test --filter "FullyQualifiedName~US11"`.

**Common mistake:** assuming every inbox has an email address. Chat-only inboxes
are valid and must not make a search fail.

**Done when:** both fields match independently, nulls are safe, default ordering
and paging remain correct, and reading does not mutate routing configuration.

<a id="us-12"></a>
## US-12: Filter inboxes by their auto-assignment setting

**User story.** As an administrator, I want to list inboxes with auto-assignment
enabled or disabled, so I can review how support channels are configured.

**Current gap and scope.** AutoAssign is already stored and returned. Add optional
`autoAssign` to listing; this does not change it. Agents keep their existing
permission to read inboxes even though the motivating user is an administrator.
Dependency: none; the filter record may be shared with US-11.

**Open first:** [InboxesController](../src/Harbor.Api/Controllers/InboxesController.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs).
Proposed test file: `US12InboxAutoAssignFilterTests.cs`.

**Acceptance criteria.** True selects enabled inboxes, false selects disabled
inboxes, omission selects both, and malformed text returns 400. Any implemented
Q search combines with the flag using AND. The setting says how new work is
configured; it is not a count or guarantee of successfully assigned conversations.

**Implementation plan.**

1. Create/reuse `InboxFilterRequest` with Q and inherited paging, then add a
   nullable boolean `AutoAssign`. Do not change the entity's non-nullable bool.
2. Bind that request record in the list action if needed, preserving all prior
   filter conditions and the workspace predicate.
3. Arrange Billing=true, Support=false, and Sales=true through the existing
   creation helper's `autoAssign` argument.
4. Write separate true, false, and omitted-filter tests before adding the new
   condition. The false test is essential; the default must not stand in for it.
5. When the option is supplied, add equality between the entity's flag and the
   supplied value. Keep this before sorting, projection, and pagination.
6. Assert true yields two inboxes, false one, and omission three. Add a second
   tenant with another enabled inbox and switch back before checking totals.
7. Verify page size one reports total two for enabled inboxes. Check invalid
   text and, if US-11 exists, one `q` combination.
8. Repeat a read as an Agent to confirm you have not unnecessarily narrowed
   the existing authorization policy.
9. Document all three option states and the difference between configuration
   and actual assignment outcomes.

**Concrete check.** `autoAssign=false` returns Support, not an unfiltered list.
Do not imitate the existing conversation `unassigned=false` semantics; this new
filter has an explicitly different contract.

**Run:** `dotnet test --filter "FullyQualifiedName~US12"`.

**Common mistake:** writing a condition only for true, causing false to behave
like omission. Test the three states independently.

**Done when:** both selections and omission work, totals stay scoped, Agents
retain read access, and no assignment or inbox-setting write occurs during listing.

<a id="us-13"></a>
## US-13: Enable or disable auto-assignment for an inbox

**User story.** As an administrator, I want to turn an inbox's auto-assignment
setting on or off, so new conversations follow our staffing preference.

**Current gap and scope.** The setting can be chosen during creation but has no
dedicated update route. Propose
`PUT /api/workspaces/{workspaceId}/inboxes/{id}/auto-assign` with body
`{ "autoAssign": true }`. Only future conversation creation consults the updated
flag; existing assignments and the rotation pointer stay as they were. Dependency: none.

**Open first:** [InboxesController](../src/Harbor.Api/Controllers/InboxesController.cs),
[ConversationStarter](../src/Harbor.Api/Infrastructure/ConversationStarter.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[AssignmentRulesTests](../tests/Harbor.Tests/Integration/AssignmentRulesTests.cs).
Proposed test file: `US13InboxAutoAssignUpdateTests.cs`.

**Acceptance criteria.** An own Admin can explicitly set true or false and gets
200 with the existing `InboxResponse`. Missing/null/non-boolean input gives 400.
Repeatedly setting the same value succeeds. Other settings and existing
conversation assignments are unchanged. Agent/anonymous/foreign-resource behavior
matches the Admin-only nested update pattern in US-06.

**Implementation plan.**

1. Add a proposed `SetInboxAutoAssignRequest` with a required nullable boolean
   AutoAssign. The nullable type plus Required distinguishes omitted input from
   an intentional false. Do not use a default false to represent both cases.
2. Add an Admin-only `SetAutoAssign` action, appending `{id:guid}/auto-assign`
   to the existing controller route prefix.
3. Load the inbox by workspace and ID; return 404 if not found in that scope.
4. Assign only AutoAssign from the validated request, save once, and return its
   existing response. Do not call `AutoAssigner` from this update action.
5. Test true, false, repeated same value, and subsequent GET persistence. Compare
   name, email, SLA settings, CreatedAt, and rotation pointer before/after.
6. Add a behavioral test: create old unassigned work while the flag is off,
   enable it, then start a new conversation. Arrange one eligible Available
   teammate and mark the bootstrap admin Away so the expected assignee is clear.
7. Assert the new conversation is assigned to that eligible teammate and the
   old conversation remains unassigned. The already existing starter handles this.
8. Disable the flag and start another conversation; it should be unassigned,
   while the previously assigned thread keeps its assignee.
9. Test missing/null/malformed bodies and the role/workspace matrix, checking
   stored values after rejected writes. Document the future-work-only effect.

**Concrete check.** Turning on the flag does not promise an assignee if nobody
is eligible. For the positive test, deliberately arrange an eligible person;
do not depend on whatever demo teammates happen to exist.

**Run:** `dotnet test --filter "FullyQualifiedName~US13|FullyQualifiedName~AssignmentRulesTests"`.

**Common mistake:** immediately reassigning existing conversations or resetting
the rotation pointer. Neither is part of updating this setting.

**Done when:** the flag round-trips, old assignments are preserved, new creation
observes the flag, and omitted input cannot silently disable it.

<a id="us-14"></a>
## US-14: Search the workspace's tags by name

**User story.** As an agent, I want to search available tags by a piece of their
name, so I can choose a useful label without reading the entire tag list.

**Current gap and scope.** Tags can be listed alphabetically but not searched.
Add optional `q` to the existing list. Keep tag creation normalization and
conversation-tag relationships unchanged. Dependency: none.

**Open first:** [TagsController](../src/Harbor.Api/Controllers/TagsController.cs),
[CannedRepliesController](../src/Harbor.Api/Controllers/CannedRepliesController.cs)
for a small search example, and
[TagAndCannedReplyEndpointsTests](../tests/Harbor.Tests/Integration/TagAndCannedReplyEndpointsTests.cs).
Proposed test file: `US14TagSearchTests.cs`.

**Acceptance criteria.** `q=bill` matches `billing` and `billing-urgent` by
case-insensitive substring. Blank/omitted Q applies no restriction. No match gives
an empty array with total zero. Results keep alphabetical order, paging metadata,
and existing Agent/Admin read access; foreign tags remain excluded.

**Implementation plan.**

1. Add optional `[FromQuery] string? q` alongside the existing `paging` parameter
   on `TagsController.List`. Avoid changing unrelated tag actions.
2. Move the existing workspace-scoped query to a variable before its ordering chain.
3. Create `billing`, `billing-urgent`, and `technical` with `CreateTagAsync`.
   Keep their returned IDs for assertions.
4. Write a search test for `bill` expecting the first two IDs, total two, and
   no `technical` result. An unchanged endpoint returning all tags should fail it.
5. For nonblank Q, trim/lowercase the query value and add a Name substring
   condition before ordering/projection/pagination.
6. Retain the existing response mapping and name ordering. This workspace's
   normalized tag names already have a uniqueness constraint.
7. Add uppercase and outer-space query cases, an empty query, no match, and a
   matching tag owned by a second workspace.
8. Walk two pages of size one and assert the expected tag appears on each, with
   total two on both responses. Document that this searches names, not conversations.

**Concrete check.** `q=BILL&page=2&pageSize=1` returns `billing-urgent` after
`billing` in the deliberately arranged fixture; total stays two. A `billing`
tag in another workspace does not change the total.

**Run:** `dotnet test --filter "FullyQualifiedName~US14"`.

**Common mistake:** searching after `ToPageAsync`. That would search only a
page-sized subset and can return too few matches or misleading totals.

**Done when:** both matching tags can be reached across pages, invalid workspace
access remains rejected, and existing tag creation/attachment tests pass.

<a id="us-15"></a>
## US-15: Find a canned reply by its exact shortcut

**User story.** As an agent, I want to find one canned reply by its shortcut, so
I can retrieve the intended response without similar shortcuts appearing too.

**Current gap and scope.** Existing Q is a substring search across several
fields. Add optional `shortcut` as an exact normalized filter on the same list.
Do not add template rendering, insert replies into conversations, or change Q.
Dependency: none.

**Open first:** [CannedRepliesController](../src/Harbor.Api/Controllers/CannedRepliesController.cs),
[CannedReply](../src/Harbor.Domain/Entities/CannedReply.cs), and
[TagAndCannedReplyEndpointsTests](../tests/Harbor.Tests/Integration/TagAndCannedReplyEndpointsTests.cs).
Proposed test file: `US15CannedReplyShortcutFilterTests.cs`.

**Acceptance criteria.** `shortcut= REFUND ` matches stored normalized `refund`
but not `refund-long`. Omitted/blank shortcut applies no additional restriction.
If Q is also supplied, both filters must match. An unknown shortcut returns an
empty list/zero total, not a detail-endpoint 404. Existing roles, arrays, and page
headers remain unchanged.

**Implementation plan.**

1. Read how Create and Update trim and lowercase shortcuts before storing them.
2. Add optional `[FromQuery] string? shortcut` to `List`, keeping its existing
   Q and PageRequest parameters. This single additional field needs no new entity.
3. Arrange canned replies with shortcuts `refund`, `refund-long`, and `hours`
   using the existing POST request record as an Admin.
4. Write the exact-match test expecting only the first reply for `shortcut=refund`.
5. For nonblank shortcut input, trim and lowercase it with the same invariant
   normalization used at write time; add a Shortcut equality condition.
6. Keep the existing Q predicate as a separate `Where` condition so the two
   filters combine using AND. Leave the old substring behavior unchanged.
7. Test mixed case, outer spaces, omitted/blank, unknown value, and combined Q
   that intentionally excludes the exact shortcut match.
8. Switch to an Agent to test reading, and arrange the same shortcut in another
   workspace to verify only the own-workspace reply is visible.
9. Document the distinction between broad Q and exact Shortcut on the list route.

**Concrete check.** If `refund` has title `Refund policy`, then
`shortcut=refund&q=policy` returns it; `shortcut=refund&q=office-hours` returns
none when that text occurs nowhere in its shortcut, title, or body.

**Run:** `dotnet test --filter "FullyQualifiedName~US15"`.

**Common mistake:** implementing Shortcut with Contains because the nearby Q
code uses Contains. This story exists specifically to request equality.

**Done when:** exact and partial searches coexist predictably, case/space
normalization matches creation, and the response contract stays a paged list.

<a id="us-16"></a>
## US-16: Find conversations with or without a subject

**User story.** As an agent, I want to find conversations missing a useful subject,
so I can review threads that are harder to identify in the queue.

**Current gap and scope.** Subject is optional but there is no presence filter.
Add `hasSubject` to conversation list and overview through their shared query.
Do not auto-generate subjects or change their stored values. Dependency: none.

**Open first:** [ConversationFilterRequest](../src/Harbor.Api/Contracts/Requests.cs),
[ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs), and
[ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs).
Proposed test file: `US16ConversationSubjectFilterTests.cs`.

**Acceptance criteria.** True selects a nonblank subject; false selects null,
empty, or whitespace-only subjects; omission does nothing. Both reads use the
same membership rule. Q search remains a separate condition, including its
existing reply-body matching and note exclusion. Bad boolean input is 400.

**Implementation plan.**

1. Add nullable `HasSubject` to the existing `ConversationFilterRequest`; it
   already inherits paging and has validation for other fields.
2. Arrange four own conversations with subjects `Invoice`, null, empty, and
   spaces. Pass the subject explicitly to `StartConversationAsync`; omitting
   the argument would use that helper's default nonblank subject.
3. Write list tests with independent expected IDs for true and false.
4. Add the presence/complement condition once in `ConversationQueries.BuildAsync`,
   keeping the initial workspace predicate intact.
5. Do not add a second condition in `ConversationsController.Overview`. Both
   consumers already call the builder, which is the behavior you want to reuse.
6. For each tested filter, call overview and compare its Total and state counts
   with the expected selected fixture, not just with the list's returned length.
7. Put `invoice` in a subjectless conversation's opening reply. Combine
   `hasSubject=false&q=invoice` and establish that reply-body search still works.
8. Add omitted, malformed, and foreign-data cases plus page size one. Read a
   subjectless conversation afterward to confirm the filter did not rewrite it.
9. Document what counts as a subject and that both reads support the parameter.

**Concrete check.** With all four own conversations Open, true overview is
Total=1/Open=1; false is Total=3/Open=3. Page size one can return one row while
the false overview still counts three. Another tenant's blank subject adds nothing.

**Run:** `dotnet test --filter "FullyQualifiedName~US16"`.

**Common mistake:** testing `Subject != null` only, which treats spaces as a
useful subject despite the explicitly agreed contract.

**Done when:** list and overview independently match the expected sets, blank
variants behave consistently, and the existing search behavior is preserved.

<a id="us-17"></a>
## US-17: Find conversations without a recorded first response

**User story.** As an agent, I want to identify conversations with no recorded
first teammate reply, including closed threads, so I can review response coverage.

**Current gap and scope.** FirstRespondedAt is already recorded, but the queue
cannot filter by its presence. Propose `awaitingFirstResponse`. The condition is
the absence of that timestamp in any state; users can add `state=Open` if they
only want currently open work. Do not redesign response timing. Dependency: none.

**Open first:** [Conversation](../src/Harbor.Domain/Entities/Conversation.cs)
(`RegisterMessage`), [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[MessagingEndpointsTests](../tests/Harbor.Tests/Integration/MessagingEndpointsTests.cs).
Proposed test file: `US17AwaitingFirstResponseTests.cs`.

**Acceptance criteria.** True means FirstRespondedAt is null; false means it is
not null; omission is unrestricted. A teammate note does not satisfy a response.
A closed unanswered thread still matches true. List and overview apply the same
rule, and existing state filters combine using AND.

**Implementation plan.**

1. Add nullable `AwaitingFirstResponse` to `ConversationFilterRequest` without
   altering existing validation or request fields.
2. Arrange A=open/unanswered, B=open/answered, C=closed/unanswered, and D=note-only.
   Use the existing message endpoint to send a real teammate Reply for B and a
   Note for D; use a valid own teammate ID and assert those requests succeeded.
3. Close C through the state endpoint without adding a teammate reply. Do not
   assume closing automatically stamps FirstRespondedAt.
4. Write true/false tests with the exact expected IDs before adding the predicate.
5. Add the null/non-null comparison once inside `ConversationQueries.BuildAsync`.
   Use the stored milestone; do not load each thread or count every message.
6. Assert overview counts against the same independent fixture expectations.
   Add `state=Open` to true and expect C to disappear while A and D remain.
7. Add an answered conversation from another tenant, omitted/malformed input,
   and page-size-one tests to protect isolation and full-set counts.
8. Document the any-state meaning prominently: this is response-history coverage,
   not necessarily work that should currently be assigned or reopened.

**Concrete check.** True selects A/C/D; false selects B. True plus Open selects
A/D. A Note on D updates activity but leaves the first-response milestone absent.
These differences make the fixture more useful than four identical Open threads.

**Run:** `dotnet test --filter "FullyQualifiedName~US17"`.

**Common mistake:** counting any teammate-authored message as a first response,
which incorrectly lets an internal note satisfy this filter.

**Done when:** answered, unanswered, closed-unanswered, and note-only examples
are all tested on both reads and the meaning can be explained without ambiguity.

<a id="us-18"></a>
## US-18: Find tagged or untagged conversations

**User story.** As an agent, I want to find untagged conversations, so I can review
work that may still need categorization.

**Current gap and scope.** The existing Tag filter selects one named tag. Add
`hasTags` to select presence of any tag, independent of its name. Use the existing
conversation-tag relationship; do not create or remove labels. Dependency: none.

**Open first:** [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs),
[ConversationTag](../src/Harbor.Domain/Entities/ConversationTag.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and
[ApiTestBase](../tests/Harbor.Tests/Integration/ApiTestBase.cs).
Proposed test file: `US18ConversationTagPresenceTests.cs`.

**Acceptance criteria.** True selects at least one tag; false selects no tags;
omission does nothing. A conversation with two tags appears once. Combining
`hasTags=false&tag=billing` produces no matches. List and overview totals remain
consistent with the whole selected set, regardless of page size.

**Implementation plan.**

1. Add nullable `HasTags` to the existing conversation filter record.
2. Arrange A with billing and urgent tags, B with one billing tag, and C with no
   tags. Use `CreateTagAsync` and `TagConversationAsync` so relationships are real.
3. Write a true test expecting A/B and a false test expecting C, using stored IDs.
4. In `ConversationQueries.BuildAsync`, use `c.Tags.Any()` for presence and its
   negation for absence. Apply it only when the nullable option is supplied.
5. Keep the existing named Tag condition as a separate predicate. Let AND
   semantics naturally produce an empty result for contradictory combinations.
6. Verify the list still includes each matching conversation's normal tag names
   in its summary; changing membership should not remove response relationships.
7. Verify overview counts A once despite two tag rows. Check page size one,
   omission, malformed boolean text, and foreign tagged/untagged conversations.
8. Document the difference between selecting a named tag and requiring any tag.

**Concrete check.** A has two tags and B one, but `hasTags=true` has Total=2,
not three. `hasTags=false&tag=billing` gives an empty array and an overview with
all zero counts. This is a valid empty intersection, not a validation error.

**Run:** `dotnet test --filter "FullyQualifiedName~US18"`.

**Common mistake:** joining conversation-tag rows and counting the resulting
rows as conversations. An existence condition avoids that duplication.

**Done when:** multi-tag conversations appear once, named filters compose
correctly, tag response data remains present, and both reads stay tenant-scoped.

<a id="us-19"></a>
## US-19: Find conversations with at least one SLA target

**User story.** As an agent, I want to identify conversations that do or do not
have a response/resolution deadline, so I can review service-target coverage.

**Current gap and scope.** Existing SLA filtering asks whether a target was
missed. Propose `hasSlaTarget` to ask whether any target exists, without evaluating
whether it is overdue. Do not reapply policies or edit deadlines. Dependency: none.

**Open first:** [Conversation](../src/Harbor.Domain/Entities/Conversation.cs),
[SlaPolicies](../src/Harbor.Infrastructure/SlaPolicies.cs),
[ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs), and
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs).
Proposed test file: `US19ConversationSlaTargetFilterTests.cs`.

**Acceptance criteria.** True selects FirstResponseDueAt non-null OR
ResolutionDueAt non-null. False selects both null; omission is unrestricted.
SlaPolicyId alone does not define target presence. Past/future deadlines and
already answered/closed work can all have targets. Both queue reads support the filter.

**Implementation plan.**

1. Add nullable `HasSlaTarget` to the existing filter record.
2. Arrange four conversations: A=no targets, B=first-response only,
   C=resolution only, D=both. In `Factory.WithDb`, set those precise nullable
   timestamp combinations and save once; no elapsed-time waits are needed.
3. Keep B's SlaPolicyId null to represent the possibility of inbox fallback
   targets. That catches an incorrect implementation based only on policy ID.
4. Write true expecting B/C/D and false expecting A before changing the query.
5. Add the OR target-presence condition in `BuildAsync`; use both-null for false.
   Do not accidentally require both targets for true.
6. Change one target from future to past in a fixture and establish membership
   is unchanged. This filter is about presence, unlike `slaBreached`.
7. Compare list IDs and overview counts with expectations, including page size
   one, a foreign target-bearing conversation, omission, and malformed input.
8. Confirm the request did not alter target timestamps or insert breach rows.
   Document that this is a read of stamped deadlines, not policy evaluation.

**Concrete check.** A response-only target and a resolution-only target both
qualify. Requiring both would mistakenly return only D; checking SlaPolicyId
could mistakenly exclude B. The four-row fixture distinguishes those mistakes.

**Run:** `dotnet test --filter "FullyQualifiedName~US19"`.

**Common mistake:** invoking SLA evaluation to answer this query. Presence can
be read directly; evaluation can write breach events and is a different operation.

**Done when:** all four target combinations pass on both reads, null policy IDs
are handled correctly, and no timing or persistence side effects are introduced.

<a id="us-20"></a>
## US-20: Filter conversations by their last-update window

**User story.** As an agent, I want conversations updated during a time window,
so I can review recently changed work even when the conversation was created earlier.

**Current gap and scope.** Creation bounds already exist. Propose `updatedFrom`
and `updatedBefore`, based on the stored UpdatedAt field. They are separate from
CreatedAt and LastMessageAt. Do not change when existing operations update any
timestamp. Dependency: none.

**Open first:** [ConversationFilterRequest](../src/Harbor.Api/Contracts/Requests.cs)
and its existing `Validate` method,
[ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs),
[Conversation](../src/Harbor.Domain/Entities/Conversation.cs), and
[ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs).
Proposed test file: `US20ConversationUpdatedWindowTests.cs`.

**Acceptance criteria.** UpdatedFrom is inclusive; UpdatedBefore is exclusive.
Either bound may be omitted. Equal/reversed supplied update bounds produce 400;
malformed dates also produce 400. Existing creation-range validation still works.
Both reads use the new window and combine it with existing filters using AND.

**Implementation plan.**

1. Add nullable DateTimeOffset properties UpdatedFrom and UpdatedBefore to the
   existing filter record; no persisted fields are new.
2. Extend `Validate` with a second, independent range check. Preserve the
   original CreatedFrom/CreatedBefore validation rather than replacing it.
3. Add `UpdatedAt >= updatedFrom` and `UpdatedAt < updatedBefore` query conditions
   when the corresponding option is supplied. Use UpdatedAt, not a similar-looking field.
4. Arrange deterministic timestamps in `Factory.WithDb`: one row before the
   lower bound, one exactly at it, one inside, and one exactly at the upper bound.
   Give creation/message times different values so the wrong column would fail.
5. Write the list expectation for the exact selected IDs and the overview
   expectation for the full selected set, even with a smaller page size.
6. Add lower-only and upper-only cases, plus equivalent timestamps with `Z`
   and `-05:00` offsets. URL-encode positive offsets if you use them in URLs.
7. Add equal/reversed, malformed, foreign-row, and valid combined creation/update
   window cases. Also rerun the existing invalid-creation-range tests.
8. Document which field is read and the half-open interval. Explain that this
   reflects the current stored UpdatedAt, not a full historical audit log.

**Concrete check.** A conversation created January 1 but updated January 5
belongs in `[January 4, January 6)`. One created January 5 but updated January 7
does not. Comparing CreatedAt instead would invert this useful test case.

**Run:** `dotnet test --filter "FullyQualifiedName~US20|FullyQualifiedName~ConversationQueueTests"`.

**Common mistake:** using inclusive upper bounds on one endpoint and exclusive
bounds on the other. Put the predicates in the shared query exactly once.

**Done when:** boundaries, equivalent instants, missing bounds, invalid ranges,
and old validation behavior are checked, and you can explain which timestamp is used.

<a id="us-21"></a>
## US-21: Sort the conversation queue by priority first

**User story.** As an agent, I want urgent conversations to appear before less
urgent ones, so I can review the most important work first.

**Current gap and scope.** Priority can be filtered, but the named sort options
only use activity or creation time. Propose `sort=PriorityFirst`: priority
descending, then LastMessageAt descending, then ID ascending. This changes
ordering, not membership, state, assignment, or the default sort. Dependency: none.

**Open first:** [ConversationPriority](../src/Harbor.Domain/ConversationPriority.cs),
[ConversationSort](../src/Harbor.Api/Contracts/Requests.cs),
[ConversationQueries.Order](../src/Harbor.Api/Infrastructure/ConversationQueries.cs),
and [ConversationQueueTests](../tests/Harbor.Tests/Integration/ConversationQueueTests.cs).
Proposed test file: `US21ConversationPrioritySortTests.cs`.

**Acceptance criteria.** Urgent precedes High, Normal, and Low. Within one
priority, newer activity comes first. Equal priority/activity uses ID ascending.
Existing sort names and their numeric values retain their meaning. Invalid sort
999 still gives 400. Overview accepts the defined sort but its counts do not change.

**Implementation plan.**

1. Read the existing priority enum: its numeric values already increase with
   urgency. Do not reorder or renumber that enum.
2. Add PriorityFirst to the API's ConversationSort enum with a new value after
   the existing members. Preserve the default RecentActivity property value.
3. Add a switch arm in `ConversationQueries.Order` with the three stated keys.
   Keep ordering on `IQueryable`; do not sort a materialized single page.
4. Arrange an old Urgent conversation, a newer Urgent one, a very recent High
   conversation, a Normal, and a Low. Pin LastMessageAt explicitly in the fixture.
5. Write an exact expected ID sequence: newer Urgent, older Urgent, High, Normal,
   Low. The High conversation's more recent activity must not override priority.
6. Add equal-priority/equal-activity rows. Use the existing tied-ID ordering test
   pattern in `ConversationQueueTests` to compare SQLite's GUID text order, not
   an assumed unrelated in-memory GUID comparison.
7. Walk pages of size two, concatenate IDs, and assert every expected ID appears
   exactly once for the static fixture. Verify totals remain unchanged.
8. Compare overview counts with/without PriorityFirst and rerun existing sorting
   and invalid-sort tests. No overview implementation change should be needed.
9. Document the complete key order and that numbered pages still can shift if
   data changes between requests; this is not a snapshot feature.

**Concrete check.** Urgent at 09:00 precedes High at 12:00. Two Urgent rows at
09:00 need the ID tie-breaker. Omitting Sort should still prioritize recent
activity as before, even if that puts a High row above an older Urgent row.

**Run:** `dotnet test --filter "FullyQualifiedName~US21|FullyQualifiedName~ConversationQueueTests"`.

**Common mistake:** ordering enum names alphabetically, which does not represent
the domain's urgency ranking.

**Done when:** the three ordering keys, static page walk, unchanged default,
existing options, and unchanged overview membership are all tested.

<a id="us-22"></a>
## US-22: Read a conversation's messages one page at a time

**User story.** As an agent using a long conversation, I want to load its messages
in small chronological pages, so I can inspect part of the thread at a time.

**Current gap and scope.** Conversation detail currently includes the full thread.
Propose `GET /api/conversations/{id}/messages`. The POST route at that path already
adds a message; a new GET action can coexist with it. Preserve the original detail
response unchanged so existing clients still work. Dependency: none.

**Open first:** [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs)
(`GetById`, `AddMessage`, and `ListAssignmentEvents`),
[Paging](../src/Harbor.Api/Infrastructure/Paging.cs),
[MessageResponse](../src/Harbor.Api/Contracts/Responses.cs), and
[MessagingEndpointsTests](../tests/Harbor.Tests/Integration/MessagingEndpointsTests.cs).
Proposed test file: `US22ConversationMessagePagingTests.cs`.

**Acceptance criteria.** Own-workspace Agents/Admins receive a paged array of
existing MessageResponse objects ordered by CreatedAt ascending, then ID ascending.
Internal notes are included for these authenticated teammates. Foreign/unknown
conversation IDs give 404; no key gives 401. Standard paging defaults, bounds,
headers, and past-end empty arrays apply. No new public endpoint is added.

**Implementation plan.**

1. Add a proposed `ListMessages` GET action with conversation ID, a `[FromQuery]`
   PageRequest parameter named `paging`, and a CancellationToken.
2. Before querying messages, check that the conversation exists with both the
   requested ID and `User.GetWorkspaceId()`. Return 404 if that scoped check fails.
3. Start a no-tracking `db.Messages` query restricted to ConversationId. The
   preceding scoped existence check establishes access to that specific parent.
4. Order by CreatedAt and then ID. Apply the existing message response mapping
   and `ToPageAsync`, passing the request cancellation token through the reads.
5. Do not fetch `GetById`'s full Messages collection and page that in memory.
   The database query must be bounded before materialization.
6. Arrange a conversation and four additional messages. Remember that the
   opening contact message already exists: the total is five, not four.
7. Pin deterministic message times and include a tied pair. Test pages of size
   two, a short final page, a past-end page, and `page=2147483647&pageSize=200`.
8. Test foreign and random parent IDs before reading/counting messages. Include
   notes and replies in the fixture, and verify another thread's messages are excluded.
9. GET the old detail endpoint to confirm its response still contains the full
   thread. Document the new bounded alternative without removing the old contract.

**Concrete check.** Five messages at page size two yield page lengths 2, 2, and
1, with total five on each response. A fourth page is empty but still reports
total five. Each ID occurs once when walking the static fixture.

**Run:** `dotnet test --filter "FullyQualifiedName~US22|FullyQualifiedName~MessagingEndpointsTests"`.

**Common mistake:** counting messages before verifying the parent belongs to the
caller. Even counts are private conversation data.

**Done when:** chronological ordering, ties, page boundaries, note visibility,
parent access, and unchanged detail behavior are demonstrated. No schema change is needed.

<a id="us-23"></a>
## US-23: Show only replies or only notes on a message page

**User story.** As an agent, I want to filter a conversation's message pages to
replies or internal notes, so I can focus on one part of the thread.

**Current gap and scope.** This extends the message-list endpoint planned in
US-22. Add `kind=Reply|Note`; do not change message creation, author validation,
email behavior, or public visibility. **Dependency: complete US-22 first.**

**Open first:** the `ListMessages` action you created in
[ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs),
[MessageKind](../src/Harbor.Domain/MessageKind.cs),
[Requests.cs](../src/Harbor.Api/Contracts/Requests.cs), and your US-22 tests.
Proposed test file: `US23ConversationMessageKindTests.cs`.

**Acceptance criteria.** Kind filters exactly to Reply or Note; omission includes
both. Unknown text and undefined numeric values return 400 validation problems.
Replies include both contact and teammate replies. Pagination totals count the
selected kind, not the whole thread. Existing parent access rules still apply.

**Implementation plan.**

1. Create/reuse a proposed `MessageFilterRequest : PageRequest` with nullable
   Kind and enum validation using the existing MessageKind enum.
2. Replace `ListMessages`' PageRequest parameter with that filter record. Preserve
   the page/pageSize query names and continue passing the record to `ToPageAsync`.
3. Keep the parent conversation's scoped access check before the message query.
4. Add Kind equality when supplied, after restricting ConversationId and before
   ordering, counting, and paging.
5. Arrange an opening contact reply, one more contact reply, one teammate reply,
   and two teammate notes. The total is five: three Reply and two Note.
6. Write `kind=Reply` expecting the three reply IDs, `kind=Note` expecting the
   two note IDs, and omission expecting all five.
7. At page size two, the Reply filter should have lengths 2 then 1 with total
   three. The Note filter should have one page of two with total two.
8. Test invalid text/999, a conversation with no notes returning an empty Note
   page, and valid-kind requests against foreign and random conversation IDs.
9. Rerun the US-22 tests so introducing the new record cannot silently break
   pagination binding or the unfiltered route. Document the new query option.

**Concrete check.** Do not count only teammate replies for Kind=Reply. AuthorType
and Kind are different fields: both customer-facing contact and teammate replies
have Reply kind, while internal notes are Note kind.

**Run:** `dotnet test --filter "FullyQualifiedName~US23|FullyQualifiedName~US22"`.

**Common mistake:** applying Kind after paging, which can return an unexpectedly
short page and a total belonging to the wrong population.

**Done when:** all three query states, invalid enums, per-kind page totals,
author-type independence, and US-22's access/default behavior pass.

<a id="us-24"></a>
## US-24: See contact email-coverage counts for the workspace

**User story.** As an administrator reviewing contact data, I want counts of
contacts with and without email addresses, so I can understand contactability at a glance.

**Current gap and scope.** Propose
`GET /api/workspaces/{workspaceId}/contacts/overview`, returning a new
`ContactOverviewResponse` with Total, WithEmail, and WithoutEmail. This first
version always summarizes the whole workspace; contact-list filters and paging
do not affect it. Existing own-workspace Agent read access is also allowed.
Dependency: none, including no dependency on US-02.

**Open first:** [ContactsController](../src/Harbor.Api/Controllers/ContactsController.cs),
[ConversationsController.Overview](../src/Harbor.Api/Controllers/ConversationsController.cs)
as the aggregate pattern, [Responses.cs](../src/Harbor.Api/Contracts/Responses.cs),
and [ContactEndpointsTests](../tests/Harbor.Tests/Integration/ContactEndpointsTests.cs).
Proposed test file: `US24ContactOverviewTests.cs`.

**Acceptance criteria.** A successful response is one JSON object, not a paged
array. WithEmail means present email text using the shared definition above;
WithoutEmail is its complement, so the two sum to Total. Empty workspace counts
are all zero. No contact details or addresses are included. Tenant/identity rules
apply equally to all three counts.

**Implementation plan.**

1. Add the response record with three integer fields to Responses.cs. Do not
   add a DbSet or entity for it; this is a calculated API result.
2. Add an `Overview` GET action with workspaceId and CancellationToken. It needs
   no contact filter record or page parameter because its scope is the whole workspace.
3. Preserve the workspace existence check and the global workspace authorization
   behavior already used by the contact list.
4. Build a workspace-scoped contacts query. Follow the conversation-overview
   pattern: group by a constant and select Total plus two conditional counts in SQL.
5. Use the same null/trimmed-text definition for both complementary groups.
   Avoid `ToListAsync` followed by counting every loaded contact in application memory.
6. Handle no aggregate row by returning a zero-filled response. An empty set is
   a valid 200 response, not null or 404 for an existing accessible workspace.
7. Arrange one contact with email, one null, one empty, and one whitespace-only
   value. Expect Total=4/WithEmail=1/WithoutEmail=3. Add foreign contacts too.
8. Test empty workspace, own Agent access, missing key, and foreign workspace.
   Requests with incidental `page` or `q` values must not make totals page- or
   search-dependent; document that this endpoint has no filter parameters.
9. Confirm there are no pagination headers or individual contact fields in this
   response. Update the API reference with the new object and an example.

**Concrete check.** `{ "total": 4, "withEmail": 1, "withoutEmail": 3 }` describes
the four own contacts above. Contacts in another workspace do not affect any
field. This is a workspace summary, unlike the conversation overview's filtered contract.

**Run:** `dotnet test --filter "FullyQualifiedName~US24"`.

**Common mistake:** counting all contacts before applying workspace scope.
An aggregate can leak other tenants' data even if it returns no personal details.

**Done when:** zero handling, complementary groups, whole-workspace semantics,
SQL aggregation, and access rules are tested without introducing persistence changes.

<a id="us-25"></a>
## US-25: See a conversation's reply and note counts

**User story.** As an agent, I want a small summary of how many replies and notes
a conversation contains, so I can understand its size without loading every message.

**Current gap and scope.** Propose
`GET /api/conversations/{id}/message-counts`, returning a new
`ConversationMessageCountsResponse` with Total, Replies, and Notes. Count the
entire thread, including its opening message; there are no filter or paging
parameters. Dependency: none; this can be implemented without US-22/23.

**Open first:** [ConversationsController](../src/Harbor.Api/Controllers/ConversationsController.cs)
(`GetById` for scope, `Overview` for aggregates),
[Message](../src/Harbor.Domain/Entities/Message.cs),
[Responses.cs](../src/Harbor.Api/Contracts/Responses.cs), and
[MessagingEndpointsTests](../tests/Harbor.Tests/Integration/MessagingEndpointsTests.cs).
Proposed test file: `US25ConversationMessageCountsTests.cs`.

**Acceptance criteria.** Own Agents/Admins get a 200 object with three integer
counts. Reply includes both contact and teammate replies; Note counts internal
notes. For the supported Reply/Note kinds, Replies + Notes equals Total. Counts
do not include another conversation. Unknown/foreign parent IDs return 404;
missing authentication returns 401. No message bodies or authors are exposed.

**Implementation plan.**

1. Add the proposed response record in Responses.cs. It is an API projection,
   not a persisted statistics table; do not cache or store counters on Conversation.
2. Add a `MessageCounts` GET action using the new path, conversation ID, and
   CancellationToken. Retain global authentication rather than marking it public.
3. First check that the conversation belongs to `User.GetWorkspaceId()`. Return
   404 for any missing/inaccessible parent before reading message counts.
4. Restrict `db.Messages` to that verified ConversationId. Use one grouped SQL
   projection for Total and conditional Reply/Note counts, following the existing
   overview pattern. Do not include the full parent thread in memory.
5. Return zero counts if the accessible parent has no message rows. Although
   normal creation supplies an opening reply, handle this defensive empty-set case.
6. Arrange a conversation with its opening contact reply, one teammate reply,
   and two notes. Expect Total=4/Replies=2/Notes=2. Create another conversation
   with many messages so a missing ConversationId predicate becomes visible.
7. Add a newly created conversation case expecting 1/1/0, plus a test-only
   empty-thread fixture using `Factory.WithDb` if desired. Do not delete real demo data.
8. Test valid caller, no key, foreign parent, random ID, and unchanged counts
   when incidental page parameters are supplied. Compare the exact response fields
   so body content cannot accidentally be returned by reusing a detail response.
9. Document that counts describe the whole thread and that notes are available
   only through this authenticated teammate endpoint.

**Concrete check.** A customer's opening reply already counts as a Reply. Adding
one teammate reply and two notes makes four messages, not three. Changing the
page-size query string must not shrink this whole-thread summary.

**Run:** `dotnet test --filter "FullyQualifiedName~US25"`.

**Common mistake:** counting AuthorType instead of Kind, which confuses “who
wrote it” with “whether it is a reply or note.”

**Done when:** exact counts, parent isolation, the opening message, defensive
empty handling, and response privacy are checked without loading all messages.

## Finishing one story without turning it into five

Use this checklist when you implement a chosen plan:

- [ ] I can state the user benefit and the exact request/response behavior.
- [ ] I inspected the existing action and reused its established patterns.
- [ ] My new tests were actually discovered; I recorded the command and result.
- [ ] I checked the intended new behavior, boundaries, and relevant access cases.
- [ ] Queries filter before paging, and responses retain their intended shape.
- [ ] Existing related tests and the full suite pass after my completed changes.
- [ ] I updated the API documentation with behavior I actually implemented.
- [ ] I reviewed the diff and did not include unrelated work or another story.
- [ ] I can explain one thing my tests do not prove.

If you get stuck, write: “I expected __; I observed __; the relevant method is
__; the smallest next check is __.” Ask a mentor about that concrete observation.
Use [the pairing dialogues](12-pairing-dialogues.md) and
[the recall guide](13-repeat-and-recall.md) as needed. A plan becoming clearer
through a small experiment is part of the exercise.
