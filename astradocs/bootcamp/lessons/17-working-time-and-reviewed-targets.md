# Lesson 17: Working time and reviewed SLA targets

This lesson accompanies S11 and S12. Check [progress](../progress.md) for verification status. Keep the [pure calculator](../../../src/Harbor.Domain/WorkingTime.cs), [shared policy application](../../../src/Harbor.Infrastructure/SlaPolicies.cs), and [reviewed-plan builder](../../../src/Harbor.Infrastructure/SlaRecalculations.cs) open.

## Begin with one customer question

“If I contact support late on Friday, when does the response allowance end?” With elapsed minutes, the answer ignores opening hours. With working minutes, the answer depends on the team's calendar. Neither is universally correct; the configured policy chooses the meaning.

A target has three separate parts:

| Part | Example | Why retain it? |
| --- | --- | --- |
| Allowance | 60 minutes | Explains how much working time was promised |
| Calendar version | Monday–Friday, 09:00–17:00 UTC | Explains which time counts |
| Deadline | Monday 09:30 UTC | Lets ordinary reads and breach checks compare timestamps |

The conversation stores the calculated deadline and applied calendar ID. It does not reinterpret old work whenever an Admin changes policy configuration. A different calendar is a new immutable row with a different ID.

## Calculate it slowly, then precisely

Draw Friday's window as a line from 09:00 to 17:00. Place a marker at 16:30. A 60-minute allowance consumes 30 minutes before Friday closes. Saturday and Sunday have no windows, so consume nothing. The remaining 30 minutes start Monday at 09:00 and end at 09:30.

Repeat with a Monday closure: Monday contributes nothing, so the result is Tuesday 09:30. Repeat with a 30-minute allowance: it ends exactly Friday 17:00. The interval is half-open for starting work, but a consumed duration may finish exactly at its closing boundary.

Now add 0.1234567 seconds to the Friday start. There are slightly fewer than 30 minutes available on Friday. The final Monday deadline must retain that fractional second. Rounding to whole minutes at each window would quietly change the promise. `WorkingTime` consumes ticks and jumps between windows; it does not loop once per minute.

Read `Normalize` first. Why reject overlapping windows? Because otherwise the same wall-clock time could be counted twice. Why reject an empty week? Because no positive allowance could finish. Why allow at most four windows per day and 100 closures? Because configuration and calculation need explicit, reviewable bounds.

Read `AddWorkingDuration` next. Identify the cursor, remaining ticks, closing boundary, and 366-day horizon. For each loop, state what decreases or advances. That is a useful review habit for every bounded algorithm: explain why it must stop.

## One calculation, several callers

`SlaPolicies.CalculateAsync` resolves the policy using the existing precedence rules, loads its scoped immutable calendar when present, and calculates the two deadlines. `Stamp` applies those values to a conversation. The preview uses the calculator without stamping a tracked entity.

Follow these paths on three separate pieces of paper:

1. Chat or new email → ConversationStarter → SlaPolicies.ApplyAsync → CalculateAsync → Stamp.
2. Simulation → CalculateAsync → response, with no conversation save.
3. Recalculation preview → CalculateAsync → stored plan; later commit → Stamp.

Use the same Friday example on each piece of paper. They should produce the same target for the same inputs. If a future feature computes its own `CreatedAt.AddMinutes(...)`, ask whether it has bypassed working calendars.

Without a calendar, elapsed-minute behavior remains the default. Snoozing does not pause the target. A response exactly at its deadline remains on time. Calendar arithmetic changes how the deadline is calculated; it does not redefine first-response and first-resolution milestones.

## Configuration change versus data migration

Create a conversation at 10:00 under a 120-minute allowance: its deadline is 12:00. Change the policy to 30 minutes. The existing conversation still says 12:00. That is intentional: changing settings and explicitly migrating existing work are separate commands.

S12 lets an Admin select 1–100 unfinished conversations and review a proposed migration. For the example, the proposal is 10:30. It is not “30 minutes after the Admin clicks commit.” The original creation time remains the starting point.

The plan records before/after targets, selected conversation versions, policy/calendar inputs, and a fingerprint. Think of the fingerprint as a compact answer to “are these still the inputs I reviewed?” It is not a lock and does not freeze the wall clock.

The commit loads fresh inputs inside a write transaction and rebuilds their fingerprint. A changed reply, state, policy, or inbox fallback invalidates the plan. It returns 409 instead of silently approving different work. An uncommitted plan expires after five minutes. A successful commit stores its result so a retry can return the same result without reapplying changes.

## Review a concrete race

At 10:05 Admin A previews conversation version V1. At 10:06 teammate B replies, changing it to V2. At 10:07 A commits. The command must reject the stale plan. It must not reason that the proposed deadline still looks plausible and skip the version check.

Repeat the race with a policy change instead of a reply. The conversation version may remain V1, so checking that token alone is insufficient. The configuration fingerprint closes that gap. Conversely, a policy fingerprint alone cannot detect the reply. Both matter.

The plan builder conservatively fingerprints workspace policy inputs, including precedence fields such as creation order. It excludes the inbox's round-robin pointer, which does not affect deadlines. Learn to distinguish relevant input changes from incidental metadata changes.

## Breach history is evidence

Applying a reviewed plan can make old work overdue immediately. Commit evaluates breaches using one execution time and stages any new unique breach events. It keeps older breach history even if a later policy gives the conversation more time. Changing today's policy cannot erase yesterday's recorded observation.

Targets, parent timestamps, new breach events, and the committed plan result save atomically. Inject failure on the plan UPDATE and inspect every selected conversation afterward. A 500 response alone is not the assertion; unchanged targets and an uncommitted plan are the assertion.

## Three learning sessions

First session: run `WorkingTimeTests` and hand-calculate every expected date before opening the assertions. Add an adjacent-window example in a disposable branch. Explain why adjacent windows are allowed while overlapping ones are rejected.

Second session: run `S11S12SlaPlanningTests`. Compare chat, email, and simulation responses for the same frozen time. Then change the policy and observe that an ordinary GET does not alter stored deadlines.

Third session: create a plan, change one input, and predict the commit result. Try a reply, a fallback change, and the exact expiry boundary. Record the last successful assertion, the expected transaction boundary, and the rows you inspected in the [journal](../journal.md).

Teach back without looking: “A duration is the allowance; a calendar says which time counts; a deadline is the result. Changing configuration does not rewrite existing promises. A reviewed migration must prove its inputs are still current.” Then repeat using the actual class and field names.
