# Review rubric and evidence of growth

Use this to choose your next exercise, not to assign yourself a job title.
For each dimension, record one example and a next step. Rate your independence:
0 = cannot explain yet; 1 = completes with guidance; 2 = completes independently;
3 = handles unfamiliar cases and helps another person reason through them.

| Dimension | Useful evidence | Review question |
| --- | --- | --- |
| Code comprehension | A request trace with real methods and data | Where does I/O actually happen? |
| Correctness | Acceptance tests and a failing-before demonstration | Which input violates your first assumption? |
| Data | Generated SQL, query plan, migration reasoning | What runs in SQL and what is materialized? |
| API design | Explicit semantics, errors, compatibility notes | Can an existing client still behave correctly? |
| Security | Role/workspace matrix and foreign-data fixture | Are totals and related IDs scoped too? |
| Reliability | Crash/race timeline and recovery behavior | Which side effect can occur twice? |
| Debugging | Hypotheses separated by an experiment | What observation would disprove your explanation? |
| Maintainability | A small, cohesive diff with clear ownership | Why does this abstraction exist now? |
| Operations | Runbook, useful signals, deployment assumptions | How will someone detect and verify recovery? |
| Collaboration | A review that changed your design or documentation | What did feedback cause you to reconsider? |

## What progression looks like in this repository

A junior practice outcome: you can implement a defined filter, explain its
signature, run a focused test, and ask a precise question when stuck.

A mid-level practice outcome: you can clarify the filter's semantics, preserve
the list/overview contract, add meaningful boundary and authorization tests, and
explain the SQL behavior without someone prescribing every step.

A senior practice outcome: you can recognize when a filter is the wrong product
solution, frame alternatives, test a performance assumption, identify rollout and
failure behavior, and help a teammate understand the decision. This work includes
uncertainty and communication, not just more classes or more endpoints.

## Review a diff in this order

1. Read the problem and acceptance criteria. Is the change solving them?
2. Identify externally visible behavior and compatibility changes.
3. Inspect tenant boundaries, domain invariants, and side effects.
4. Inspect database work, concurrency assumptions, and failure behavior.
5. Read tests against independent expected outcomes.
6. Check names, duplication, and documentation after correctness is understood.

Leave comments that name a trigger, consequence, and remedy or question. For
example: “Two rows can share `LastMessageAt`; paging by this column alone leaves
their order unspecified. Could we add a unique tie-breaker and a tied-time test?”
This gives the author something concrete to assess.

## A compact PR description

Use the [feature brief](templates/feature-brief.md) while planning. For the final
PR, lead with the problem and resulting behavior, then validation and limits.
Example:

> Agents can now select a creation window and get counts for the complete
> filtered queue. List and overview share a query, and every ordering has an ID
> tie-breaker. Tests cover filter semantics, tenant isolation, date boundaries,
> and extreme page numbers. Offset pagination still observes a changing dataset.

Replace generic “tested” with the commands you ran and their results. Do not
claim performance improvements unless you measured them.

## Teach-back prompts

- Explain a transactional outbox to someone who knows HTTP but not distributed systems.
- Show why a GUID is not authorization.
- Explain why correct static pagination can still repeat a row under writes.
- Describe a test that passed while a meaningful failure remained untested.
- Present a design you simplified after learning more about the problem.

Revisit the same dimension with a different example two weeks later. Transfer
to a new situation is stronger evidence than remembering one implementation.
