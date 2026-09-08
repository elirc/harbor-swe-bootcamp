# Harbor implementation bootcamp

The learning material is the main deliverable. The 75 implemented stories from the [junior](../16-junior-user-stories.md), [midlevel](../17-midlevel-feature-stories.md), and [mid/senior](../18-mid-senior-feature-stories.md) backlogs form a sequence of explained, tested changes.

Those backlog documents preserve their original planning language. Implementation status lives in the [progress tracker](progress.md), and evidence and decisions live in the [working journal](journal.md). A planned endpoint is not an implemented endpoint. A completed code change is not automatically a verified lesson.

## Your learning loop

1. Read the lesson's user problem and predict the result of its example request.
2. Follow the linked request contract, controller/query, entity, and test. Name the responsibility of each file.
3. Run the small focused test group and compare its actual assertions with your prediction.
4. Try the exercise in a separate local change. Break one rule deliberately, observe the failing test, then restore the rule.
5. Explain the behavior without looking at the code. Return tomorrow and explain the failure case instead.

Different explanations are intentional. Each lesson uses a request trace, a concrete dataset, a code-reading guide, and questions with answers. You can reread the same behavior from another angle instead of forcing yourself through increasingly abstract explanations.

## Curriculum

| Stage | Stories | What you practice |
| --- | --- | --- |
| Foundations | US01–US25 | Request binding, filtering, nullable values, SQL queries, pagination, authorization, aggregates |
| Feature ownership | M01–M30 | Shared rules, migrations, private resources, concurrency, transactions, product contracts |
| System behavior | S01–S20 | Durable replay, leases, scheduling, files, snapshots, recovery, safe upgrades |

The curriculum follows small coherent implementation groups. M27 follows M26; S04 follows S03. Features touching the same workflow share that workflow rather than introducing competing rules. Later lessons explicitly revisit earlier decisions when the requirements become more demanding.

## Lessons

- [01: Contact queries](lessons/01-contact-queries.md) - US01-US05.
- [02: Directories and settings](lessons/02-directories-and-settings.md) - US06-US15.
- [03: Queues and aggregates](lessons/03-queues-and-aggregates.md) - US16-US25.
- [04: Personal work and stale tabs](lessons/04-personal-work-and-stale-tabs.md) - M01-M04.
- [05: Batches and operational reads](lessons/05-batches-and-operational-reads.md) - M05, M06, M08-M10.

- [06: Customer context and explanations](lessons/06-customer-context-and-explanations.md) - M07, M11-M16.

- [07: SLA decisions and time boundaries](lessons/07-sla-decisions-and-time-boundaries.md) - M17-M18.
- [08: Reports and cohorts](lessons/08-reports-and-cohorts.md) - M19-M21.
- [09: Articles, history, and feedback](lessons/09-articles-history-and-feedback.md) - M22-M25.
- [10: Templates and delivery recovery](lessons/10-templates-and-delivery-recovery.md) - M26-M30.
- [11: Ingress receipts and transaction ownership](lessons/11-ingress-receipts-and-transaction-ownership.md) - S01-S02.
- [12: Webhook ownership and uncertainty](lessons/12-webhook-ownership-and-uncertainty.md) - S03-S04.
- [13: Credential lifecycle and upgrades](lessons/13-credential-lifecycle-and-upgrades.md) - S05.
- [14: Reviewed merges and staged imports](lessons/14-reviewed-merges-and-staged-imports.md) - S06-S07.
- [15: Routing and watched replies](lessons/15-routing-and-watched-replies.md) - S08-S09.
- [16: Approved content and durable schedules](lessons/16-approved-content-and-durable-schedules.md) - S10 and S17.
- [17: Working time and reviewed targets](lessons/17-working-time-and-reviewed-targets.md) - S11-S12.
- [18: Claims, checklists, and inbox lifecycle](lessons/18-claims-checklists-and-inbox-lifecycle.md) - S18-S20.
- [19: Private files and reconciliation](lessons/19-private-files-and-reconciliation.md) - S13.
- [20: Durable events and resume cursors](lessons/20-durable-events-and-resume-cursors.md) - S14.
- [21: Export jobs and consistent snapshots](lessons/21-export-jobs-and-consistent-snapshots.md) - S15.
- [22: Safe configuration audits](lessons/22-safe-configuration-audits.md) - S16.

Use the [practice schedule](practice-schedule.md) to revisit each concept in shorter sessions, and the [debugging guide](debugging-guide.md) to turn failures into evidence.

Use [checkpoint review](checkpoint-review.md) to explain each group through concrete fixtures, request traces, and failure diagrams before moving on.

Use the [codebase atlas](codebase-atlas.md) to find each feature's controller, shared workflow, and evidence. It includes the same write explained as a user action, a data change, and a failure timeline.

Use the [verification guide](verification-guide.md) for focused test filters, full-check commands, and a short personal lab notebook template.

Use [run and operate](run-and-operate.md) to start a disposable lab, enable workers explicitly, and rehearse restart and cleanup behavior.

## Evidence standard

Every completed story needs implemented acceptance criteria, relevant tests that actually ran, API documentation, and an explained learning artifact. Schema changes also need a migration and upgrade checks. Concurrency claims need appropriate separate-connection tests; a sequential HTTP test cannot prove them.

The tracker records all 75 stories as verified by the 857-test Release suite. The journal distinguishes commands run, results observed, and future work. Existing unrelated workspace edits are preserved.
