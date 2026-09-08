# Grow as an engineer with Harbor

For smaller steps and more repetition, use [AstraDocs](../../astradocs/README.md)
first. It teaches the same core codebase ideas through stories, diagrams,
annotated code, pairing conversations, and recall exercises.

Start with [your first 90 minutes](first-90-minutes.md). You do not need to
understand the whole repository before making a useful change.

Harbor is a practice backend for learning C#, HTTP, SQL, testing, and engineering
judgment. The aim is to build evidence that you can understand a system, change
it safely, diagnose failures, and explain your decisions. Completing these pages
does not by itself establish a job level: senior work also involves ownership,
collaboration, and decisions under real operational constraints.

## Choose your entry point

| If this sounds like you | Start here | First thing to produce |
| --- | --- | --- |
| I can write small programs but this repo feels overwhelming | [First 90 minutes](first-90-minutes.md), then [C# through Harbor](csharp-foundations.md) | A passing test and a request-flow sketch |
| I can add an endpoint by copying another one | [Request tracing](request-tracing.md), then [worked feature](worked-feature.md) | An explanation of each boundary crossed by a request |
| My features work but edge cases surprise me | [Testing](testing-workshop.md) and [debugging](debugging-labs.md) | A regression test that fails for a real defect |
| I own features but struggle with design/performance | [Data](data-and-queries.md) and [reliability](reliability.md) | A measured query experiment and a failure timeline |
| I want to practice senior-level ownership | [Capstones](capstones.md), [security](security-and-operations.md), [review rubric](review-rubric.md) | A design, rollout plan, runbook, and reviewed implementation |

Use the [roadmap](roadmap.md) to sequence the work. Use the
[glossary](glossary.md) whenever a term interrupts your understanding.

## How to use each session

1. **Predict:** write down what you expect before running code.
2. **Trace:** locate the actual implementation and its nearest test.
3. **Change:** make one small behavior change or controlled experiment.
4. **Verify:** run the narrow test, then the relevant broader suite.
5. **Explain:** describe the result and a limitation without reading the code.
6. **Record:** capture evidence in the [learning journal](templates/learning-journal.md).

Allow 45–90 minutes for an exercise. If you spend 20 minutes stuck, write the
exact observed behavior, your hypothesis, and the smallest experiment that would
separate two explanations. That note is useful progress.

## What has already been built for you to study

The [worked feature](worked-feature.md) explains this repository's new queue
overview, creation-date filters, selectable sorting, shared query component, and
overflow-safe pagination. These are implemented features with tests. Read the
acceptance criteria, attempt a small extension, and only then inspect the code.

The [capstone backlog](capstones.md) contains **future exercises**, not capabilities
the API currently promises. Keep that distinction when describing the project
in a portfolio or interview.

## Learning map

| Topic | Guide | Concrete source to read |
| --- | --- | --- |
| C# records, nulls, async, interfaces | [Foundations](csharp-foundations.md) | `Requests.cs`, `Conversation.cs`, `WebhookSender.cs` |
| HTTP, binding, DI, tenant boundaries | [Trace a request](request-tracing.md) | `Program.cs`, `WorkspaceScopeFilter.cs`, `ConversationsController.cs` |
| Query composition, SQL, paging | [Data and queries](data-and-queries.md) | `ConversationQueries.cs`, `SegmentQueries.cs`, `Paging.cs` |
| Implementing a feature end to end | [Worked feature](worked-feature.md) | `ConversationQueueTests.cs` |
| Test design and harness limits | [Testing workshop](testing-workshop.md) | `HarborApiFactory.cs`, `MigrationDriftTests.cs` |
| Hypotheses, reproduction, diagnosis | [Debugging labs](debugging-labs.md) | `Paging.cs`, `Conversation.cs`, `WebhookDispatcher.cs` |
| Transactions, races, retries | [Reliability](reliability.md) | `HarborDbContext.cs`, `Webhooks.cs`, `WebhookDispatcher.cs` |
| Security and operational ownership | [Security and operations](security-and-operations.md) | `ApiKeyAuthenticationHandler.cs`, `RequestLoggingMiddleware.cs` |
| Independent feature ownership | [Capstones](capstones.md) | Multiple layers; scope is part of the exercise |
| Reviewing and communicating | [Review rubric](review-rubric.md) | Your diff, tests, and design record |

The existing [architecture](../architecture.md), [API reference](../api-reference.md),
[testing reference](../testing.md), and [decision records](../adr/README.md) describe
the system. These learning guides teach you how to investigate and evolve it.

## Working with an AI tutor

Ask for help at the smallest level that gets you moving. Try:

> I am doing the paging exercise. Ask me to predict the result before showing a
> solution. Give one hint at a time. After I write a test, review what it proves
> and what it misses. Let me explain the fix before you suggest code.

For design practice:

> Review my design against these acceptance criteria. Challenge one assumption
> about concurrency and one about tenant isolation. Separate observed defects
> from hypothetical risks. Ask me what evidence would change my decision.

For consolidation:

> Quiz me with a new example of the same concept. Do not reuse the exact variable
> names or test data. Ask me to explain the tradeoff to a teammate.

After receiving help, close the explanation and reconstruct the reasoning in
your own words. The evidence is what you can now do independently.

## Keep a small evidence portfolio

Maintain one journal entry per session, one [feature brief](templates/feature-brief.md)
per meaningful change, and an [incident report](templates/incident-report.md) for
one debugging lab. Track reproducible commands and results, not just hours spent.
Revisit the [rubric](review-rubric.md) after every two completed exercises.
