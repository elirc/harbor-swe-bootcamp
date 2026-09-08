# 08: Watch one conversation change over time

**Remember today:** a conversation's current state and its history are different.

## First explanation: follow the invoice story

Maya's invoice conversation starts Open. Ada snoozes it until tomorrow. Maya
sends a new reply before then, so it reopens. Ada writes an internal note, sends
a reply, and closes it. Later, Maya replies again and it reopens again.

The same conversation record survives these changes. Closing is a state change,
not deletion. Reopening does not erase the first response or first resolution.

## Second explanation: a behavior table

Read [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs) beside this
table. Focus on `Open`, `Snooze`, `Close`, and `RegisterMessage`.

| Event | What to remember |
| --- | --- |
| Explicitly open | State becomes Open; snooze and current closed time clear |
| Snooze into the future | State becomes Snoozed; wake time is recorded |
| Snooze at or before now | Domain rule rejects it |
| Close | State becomes Closed; current close time changes; first resolution is set if absent |
| Contact reply on closed/snoozed work | Conversation reopens |
| First teammate reply | First-response time is recorded |
| Teammate internal note | Activity timestamps change; state and first-response time do not |

A note does have an effect on activity. Saying “notes do nothing” would be wrong.
The narrower rule is that they do not reopen a conversation or satisfy first response.
The API also rejects notes authored as contacts.

## Third explanation: two clocks on the wall

Imagine one label says **first time we resolved this** and another says **most
recent close**. Those correspond to `FirstResolvedAt` and `ClosedAt`.

At 10:00, close the conversation: both can be 10:00. At 11:00, reopen it:
`ClosedAt` clears, while `FirstResolvedAt` stays 10:00. At 12:00, close again:
`ClosedAt` becomes 12:00; `FirstResolvedAt` stays 10:00.

Read the assignments in `Close`: `ClosedAt = now` versus
`FirstResolvedAt ??= now`. One replaces a value; one fills it only when absent.

## Deadlines without jargon

An SLA is a service target, such as “send the first response within an hour.”
Harbor stores due timestamps. A target is breached when the relevant time is
strictly later than the deadline; exactly on the deadline is not late.

A closed conversation can still have missed its resolution target. “Closed now”
describes current state. “Resolved late” describes an outcome in its history.
That is why the SLA-breached filter can include closed work.

The clock for these targets runs from conversation creation. Reopening or
changing priority does not start an entirely fresh creation clock.

## One feature the timestamp alone does not provide

The current host does not run a periodic sweep to reopen due snoozed conversations.
Recording `SnoozedUntil` is not the same as scheduling a worker. A contact reply
or explicit state action can reopen it; automatic sweeping is a future exercise.

## Check the story

Ada writes a note on a closed conversation. Does it reopen? Does the note become
the first teammate reply? Does all activity stay unchanged?

<details>
<summary>Check each part separately</summary>

It stays closed. The note does not set first-response time. Activity timestamps
do change. Keep all three statements together so the shorthand does not become
a misleading rule.

</details>

Repeat: **current state, historical milestones, and activity are different values**.
Next: [access and errors](09-access-and-errors.md).
