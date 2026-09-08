# Lesson 22: Safe configuration audits

S16 records selected committed configuration changes. Read [typed audit helpers](../../../src/Harbor.Infrastructure/ConfigurationAudits.cs), [the reader](../../../src/Harbor.Api/Controllers/ConfigurationAuditController.cs), and [audit tests](../../../tests/Harbor.Tests/Integration/S16ConfigurationAuditTests.cs). Check [progress](../progress.md) before treating staged behavior as verified.

## Begin with an investigation

An Admin asks why a teammate stopped receiving automatic assignments. A useful answer might be: “At 10:15, teammate A changed their availability to Away and capacity from 2 to 4.” A request log saying “PUT availability returned 200” is less specific. A full serialized teammate object would contain far more data than this investigation needs.

The audit feature records a narrow set of changes: teammate availability/capacity, team membership additions/removals, and canned reply create/update/delete. It does not claim to audit every application write, reconstruct pre-feature history, or provide a tamper-proof external archive.

Coverage is a product contract. Start every audit review by listing the supported mutations. An investigator must know whether an absent event means “no covered change committed” or “this kind of change was never covered.”

## What belongs in an event?

| Covered change | Recorded values | Deliberately omitted |
| --- | --- | --- |
| Availability/capacity | Actor, target, changed fields, old/new allowed values | Key hashes and other teammate fields |
| Membership | Actor, team and teammate IDs, addition/removal | Full directory objects |
| Canned reply | Actor, reply ID, action, changed-field names | Title, shortcut, and body contents |

For a canned reply, the audit can say Body changed without copying either version of the body. That is enough to identify the kind of mutation while honoring the stated content boundary. Article revision history has a different purpose and contract; do not assume every history feature should preserve full content.

Historical IDs remain raw values. Deleting a canned reply does not erase its earlier audit rows. The workspace is the retention boundary. A current display name can be a decoration, but it must not replace the historical actor ID as the authoritative identity.

## Trace one availability update

The controller authenticates and scopes the target. An Agent may change their own availability; only Admins may change another teammate's. Reading the audit remains Admin-only. A valid Agent is still the recorded actor of their own change.

Inside the write transaction, load current availability and capacity. Compare those values with the requested values. The typed helper stages an event only when something actually changes. Then mutate the teammate and save configuration plus audit together.

Why load “before” values inside the transaction? If another request changed capacity after an earlier read, the audit could otherwise claim the wrong old value. Correct attribution includes both the authenticated actor and the actual state transition.

Repeat this as a three-row example: old teammate values, proposed teammate values, and one audit event naming the differences. Then remove the differences. An unchanged request should create no event.

## Membership and normalized no-ops

Adding an existing team member is a no-op in this API. It should not create another “member added” event. The membership check, insertion, and audit staging share one transaction, so competing adds cannot both report a new committed membership.

Canned reply shortcuts are normalized before comparison. A request that differs only in normalization but leaves the stored fields unchanged should not create an update event. Compare what will actually be persisted, not the raw request text.

Delete commands preserve history even though the current entity goes away. The audit rows do not depend on a foreign key to the deleted reply or membership. Try reading the audit after deleting the target; that is the concrete reason for retaining raw historical IDs.

## Why explicit helpers are useful

`ConfigurationAudits.Availability`, `Membership`, and `CannedReply` accept only the information each event is allowed to retain. The canned helper filters field names to Shortcut, Title, and Body. It has no parameter for old/new body text.

A generic “serialize this entire entity” helper would be convenient to call and difficult to trust. Adding a new secret-bearing property to the entity could silently expand the audit payload. Explicit typed inputs make the allowed boundary visible during review.

This is a general design skill: when a subsystem has a narrower information contract than the source object, give it a narrow input type or explicit arguments. Do not rely on every future caller to remember which properties are inappropriate.

## Test the commit boundary and the information boundary

The rollback test injects failure on the teammate UPDATE. After the request fails, both the original teammate values and empty audit selection remain. That proves the audit did not commit separately from the mutation.

The content test creates and changes a canned reply containing distinctive secret-like strings. It verifies that neither stored allowed-change JSON nor the HTTP audit response contains them. Use distinctive strings so the assertion can detect accidental serialization, not merely an unrelated missing word.

The same test checks actual actor IDs, no-op suppression, membership events, deletion history, and invalid filters. Date filtering uses inclusive From and exclusive Before. Frozen timestamps let you test exact boundaries without relying on a short real-time delay.

The reader performs workspace filtering in SQL and sorts time descending, then ID for stable ties. Pagination changes how much history a response returns; it does not change which committed events exist.

## Three small practice sessions

Today, draw the coverage table from memory. Pick one mutation and find its permission check, before-value capture, audit helper, and shared save. Explain each step in a single sentence.

Tomorrow, run the no-op and secret-exclusion tests. Change a harmless request field in a disposable experiment and predict whether the audit count or changed-field list should change. Restore the experiment afterward.

Later, rehearse an investigation: an Agent changed availability, an Admin removed a membership, and a canned reply was edited then deleted. Use only audit IDs, actions, allowed values, and timestamps to explain what is known. Explicitly identify what this feature cannot reconstruct.

Teach back: “An audit row describes a committed covered mutation and its real actor. It stores only the allowed facts, survives target deletion, and rolls back with a failed write.” Support each phrase with a test or a concrete code path before calling the feature done.
