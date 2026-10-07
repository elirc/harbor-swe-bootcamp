# Review findings and the teaching changes

This review focuses on making Harbor useful for learning to own backend changes.
It is a targeted code review, not an exhaustive security or production-readiness
assessment. Findings below distinguish completed changes from future exercises.

## Changes completed

| Finding | Evidence in the previous code | Change and teaching value |
| --- | --- | --- |
| Large valid page numbers could overflow offset arithmetic | `Paging` multiplied two `int` values before `Skip` | Widen before multiplying, return empty past-end pages; learn boundary reasoning |
| Conversation ordering had no explicit tie-breaker | List ordered only by `LastMessageAt` | Add ID ordering to all three sort modes; learn total order versus snapshot |
| New queue counts would duplicate membership rules | Filtering lived entirely inside the list action | Extract `ConversationQueries`; learn responsibility-based refactoring |
| Conversation filtering depended on a second controller | It called `SegmentsController.ContactIdsQuery` | Move segment helpers to Infrastructure; learn dependency direction |
| Agents could not choose a creation window or creation ordering | No corresponding conversation query inputs | Add validated half-open bounds and sort selection |
| Clients lacked counts across the full filtered queue | Only paged rows and a total header were available | Add SQL overview with total/state/unassigned counts |
| Documentation assumed readers could navigate a large backend | Reference docs lacked staged exercises and evidence criteria | Add a guided curriculum, worked feature, labs, capstones, and templates |
| Fresh restore depended on machine configuration | This machine had no configured package sources | Add repository `NuGet.Config` |
| Restore surfaced vulnerable transitive dependencies | NuGet audit identified OpenAPI and native SQLite packages | Explicitly select updated versions in their existing major lines; audit resolved packages |
| Test helpers hid inherited helpers | Two compiler warnings identified duplicate/confusing helper names | Reuse the shared tag helper and name raw assignment requests distinctly |

The new HTTP test matrix covers all conversation filters on both reads with
independent expected results, including other-tenant data. It also covers date
boundaries, equivalent offsets, invalid sorts/ranges, tied page boundaries, and
extreme pages. The SQL-translation test continues to guard segment composition.

## Deliberate remaining practice opportunities

> **Status note (checked 2026-10-06):** this section predates the 75-story
> bootcamp. Since then, paged messages (`GET /api/conversations/{id}/messages`),
> an admin-triggered `POST .../conversations/wake-due` sweep, `ExpectedVersion`
> stale-edit checks on subject/draft/checklist edits, and bounded leased webhook
> claims have been added. What remains true: `GetById` still returns the whole
> thread, and nothing runs the snooze sweep or webhook dispatch on a schedule.
> See the status table in [capstones](capstones.md).

**Large detail responses:** `GetById` still loads the full conversation thread.
The new overview avoids that work, but does not change the detail contract.
[Paged messages](capstones.md) is a bounded next feature with a compatibility lesson.

**Older reports:** reporting endpoints still materialize filtered conversations
for some calculations and have their own filter contract. Measure a representative
report before changing its aggregation or adding indexes.

**Scheduling:** SLA evaluation and webhook dispatch are manually triggered.
There is no periodic snooze wake-up sweep. A stored deadline is not a scheduler.
The sweep and worker capstones explicitly address these missing capabilities.

**Concurrency scope:** tokens protect overlapping tracked saves, not every stale
human edit across separate requests. Webhook sends can precede a failing status
save, so receiver deduplication remains necessary. These are distinct concurrency
problems requiring distinct designs.

**Time and cancellation:** the new query paths accept a supplied evaluation time
and forward request cancellation, but much of the existing API still reads the
wall clock directly and does not forward cancellation consistently. Extend these
seams incrementally with tests.

**Outbound destination policy:** configurable webhook URLs reach the HTTP sender.
Shape validation is not a complete deployment egress policy. Review that boundary
before exposing the service to untrusted tenants.

**Operational claims:** development startup applies migrations and seeds demo
data; production schema rollout is external to this host. The testing docs now
state that distinction correctly. Health probes do not establish worker progress.

For each opportunity, write a small acceptance contract and decide the evidence
needed before choosing an abstraction. The [rubric](review-rubric.md) is the review
checklist for that work.
