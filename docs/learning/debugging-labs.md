# Debugging labs: turn a symptom into evidence

Use a practice branch. Keep experiments local and synthetic. Record each lab with
the [incident template](templates/incident-report.md). A useful debugging loop is:
observe, propose two explanations, design a separating experiment, then update
your explanation. Editing until a test turns green is not the same thing.

## Lab 1: a huge page returns earlier rows

Level: junior to mid. Time: 45–60 minutes. Code: `Paging.cs`.

The overflow bug is fixed in this update. Reproduce the old arithmetic in an
isolated test without changing the shared helper:

```csharp
var page = int.MaxValue;
var size = 200;
var brokenOffset = unchecked((page - 1) * size);
var correctOffset = ((long)page - 1) * size;
```

Prediction: should either offset be negative? Inspect both values. The broken
result is `-400`; the correct result is `429496729200`. A negative offset can
cause a paging path to behave as though nothing was skipped.

Now compare these hypotheses: “the controller ignored the query string” versus
“the inputs bound correctly but arithmetic overflowed.” Use the page header and
the intermediate offset to distinguish them. The metadata can be correct while
the row selection is wrong.

Deliverable: explain why widening after multiplication fails, why the helper
returns an empty page before casting to `int`, and why both SQL and in-memory
paging need regression coverage. Read `PagingOverflowTests` only after writing
your own expected result.

## Lab 2: a closed conversation is still SLA-breached

Level: mid. Time: 45 minutes. Code: `Conversation.cs`, `ConversationQueries.cs`.

Create a conversation whose resolution due time is 10:00 and first resolution
time is 10:01. It is closed now. Predict whether `slaBreached=true` includes it.
Distinguish two hypotheses: “closed work cannot be breached” versus “the filter
includes historical missed targets.” Trace `IsResolutionBreached` and the SQL
predicate. This is a misunderstanding to diagnose, not automatically a bug.

Deliverable: a fixed-time test showing on-time and late closed conversations,
plus a short API explanation. State what a separate “currently overdue open work”
filter would mean if a product manager requested one.

## Lab 3: the webhook arrived twice

Level: mid to senior. Time: 60–90 minutes. Code: `WebhookDispatcher.cs`.

Use the fake sender, not a real receiver. Draw this sequence: delivery is pending,
receiver accepts it, process fails before saving the successful status, dispatcher
retries. Ask whether a database concurrency token can undo the first HTTP request.
Then consider two dispatchers loading the same pending row before either saves.

Deliverable: an event timeline identifying what is durable at each step, a test
design for the send-before-save window, and a receiver deduplication design using
the delivery ID. Do not claim exactly-once delivery because a retry test passed.
See [reliability](reliability.md) for the current guarantees and limits.

## Lab 4: tests pass but startup cannot find a column

Level: mid. Time: 60 minutes. Code: `MigrationDriftTests`, `HarborApiFactory`.

Reason through adding a persisted property without a migration. Why might HTTP
tests using `EnsureCreated` still work? Run the migration drift tests, then read
their failure message in a controlled practice change. Restore the property or
complete the migration on the practice branch afterward.

Deliverable: explain the difference between creating a schema from the current
model, replaying migrations into an empty database, and upgrading populated data.
List the evidence each would provide for a schema-changing feature.

## Lab 5: a queue request gets slower as a tenant grows

Level: mid to senior. Time: two sessions. Code: `ConversationQueries`, `Paging`.

Start with two hypotheses: the count query scans many rows, or loading the page
and its tags dominates the request. Measure those operations separately using
synthetic datasets and EF command logging. Warm up once, repeat measurements,
and report the distribution instead of one fastest value.

Deliverable: query plans, row counts, result size, before/after timings, and one
chosen change. If an index does not help substring search, record that result.
A disproved hypothesis is useful evidence. Avoid logging customer message bodies
or real API keys while collecting diagnostics.

## A good debugging explanation

Write: “The observed symptom was X. I considered A and B. Experiment C ruled out
A because D. The root cause was E. Change F addresses it, test G catches its
return, and limitation H remains.”

If you cannot name an experiment that would disprove your current explanation,
pause before changing more code.
