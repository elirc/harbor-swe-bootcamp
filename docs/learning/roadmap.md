# A roadmap based on demonstrated skills

Use these as stages, not a promotion schedule. One stage may take several
sessions. Progress when you can produce the evidence without copying a solution.
Keep a short list of uncertainties; independent engineers know what they have
not yet established.

| Stage | Study | Practice | Evidence to move on |
| --- | --- | --- | --- |
| 1. Read and run | [First session](first-90-minutes.md), [C#](csharp-foundations.md) | Run one endpoint and trace its types | Explain a request from JSON to response |
| 2. Own a small change | [Request tracing](request-tracing.md) | Add a combined filter test and invalid-input test | A small diff with a failing-before/passing-after demonstration |
| 3. Protect behavior | [Testing workshop](testing-workshop.md) | Exercise boundary values, empty sets, and other tenants | Explain why each test catches a distinct defect |
| 4. Work with the database | [Data and queries](data-and-queries.md) | Inspect SQL, compare page and overview, measure one query | SQL output plus a justified performance conclusion |
| 5. Refactor safely | [Worked feature](worked-feature.md) | Extend a shared filter without duplicating it | Both API paths agree and existing contracts pass |
| 6. Diagnose a failure | [Debugging labs](debugging-labs.md) | Reproduce overflow and a retry failure | Hypothesis log, regression test, root-cause explanation |
| 7. Reason about races | [Reliability](reliability.md) | Draw stale-write and duplicate-delivery timelines | Identify the transaction boundary and unresolved risk |
| 8. Own a feature | [Capstones](capstones.md) | Implement paged thread messages or a snooze sweep | Brief, implementation, tests, docs, failure behavior |
| 9. Own operation | [Security/operations](security-and-operations.md) | Review an endpoint and rehearse an incident | Tenant test matrix, runbook, measurable recovery criterion |
| 10. Lead a design | [Rubric](review-rubric.md) | Review alternatives with a peer and revise one choice | A reasoned ADR, rollout plan, and evidence-driven revision |

## A sample weekly rhythm

Use three sessions per week if that fits your schedule:

- Session A: read a small path, predict behavior, run an experiment.
- Session B: implement one acceptance criterion and its tests.
- Session C: inspect your diff, explain a tradeoff, and write the journal entry.

Reserve time for review. Writing code for an hour and inspecting it for ten
minutes often teaches more than spending the whole session adding features.
If a topic is confusing, reduce the exercise until only one concept changes.

## Stage 1–3: build a reliable local feedback loop

Practice reading C# without translating every symbol into another language.
Distinguish `Guid` identifiers from authorization, nullable values from invalid
values, and a `Task` from a completed result. Learn to make a test fail for the
reason you intended. Make your first changes in one request record or one query
condition so you can inspect the entire diff.

Suggested exercises:

- Explain why `CreatedBefore` uses `<` and write the exact-boundary example.
- Predict what `unassigned=false` does, then check the implementation.
- Send an invalid sort and locate where the 400 originates.
- Compare an anonymous request, another workspace's route, and a foreign by-ID resource.
- Explain why a contact note is rejected and why a teammate note does not reopen a conversation.

## Stage 4–6: become responsible for a change

Now include database behavior and compatibility in your definition of done.
An API returning correct rows on a small fixture can still load too much data,
produce inconsistent pages, or leak another tenant's totals. Inspect the actual
query and test its boundaries. Refactor only when you can name the shared
responsibility and the behavior that must remain stable.

Suggested exercises:

- Add `createdFrom` to a hand-built query, then move it into the shared builder.
- Show why a count taken after `Take(2)` answers a different question.
- Create tied timestamps and walk every page.
- Explain why a tracked write entity and a no-tracking read serve different purposes.
- Measure a query with 100, 1,000, and 10,000 rows; state what your local experiment cannot establish.

## Stage 7–10: practice ownership beyond implementation

Write failure timelines before adding concurrency mechanisms. Ask who owns the
retry, what happens after a process crash, which data is sensitive, and how an
operator will know a feature is healthy. A senior-level decision might remove
scope after measuring cost; it is not necessarily a larger abstraction.

Choose one capstone and keep it small enough to finish. Review the design before
coding, record assumptions, and revise when tests or measurements contradict
them. Have someone else use your setup instructions. Watch where they get stuck
and improve the instructions without blaming the reader.

## Review checkpoints

After stage 3, explain a single endpoint to a new contributor in five minutes.
After stage 6, demonstrate a bug, fix, and regression test in ten minutes.
After stage 10, present an option you rejected, your evidence, the chosen
approach's limitation, and a condition that would cause you to reconsider.

Use [the rubric](review-rubric.md) to assess independence and quality of evidence.
Do not mark a skill complete merely because you recognize its vocabulary.
