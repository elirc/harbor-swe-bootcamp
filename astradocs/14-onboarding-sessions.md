# 14: An onboarding plan in small sessions

**Remember today:** finish one observable task, then decide whether to continue.

Try 15–25 minutes for reading, with extra time for setup or test runs. Session
numbers are an order, not calendar days. Repeat a session or split it in half
whenever useful. This plan works alone or with a mentor.

## Repeat this routine

Recall the last idea before reopening its page. Read one new section. Find its
source method. Predict one behavior and check it. End with a one-sentence
explanation and a note about what remains confusing.

Use drawings, spoken explanations, or written notes for recall. Understanding
the behavior matters before polishing the technical vocabulary.

## The sessions

| Session | Revisit first | New activity | Concrete completion check |
| --- | --- | --- | --- |
| 1 | Nothing required | Read [start here](01-start-here.md) | Explain contact, teammate, conversation, and message using the invoice story |
| 2 | Say who Maya and Ada are | Open [the folder map](02-folder-map.md) | Find the conversation entity and its unit test |
| 3 | Name the source responsibilities | Follow [picture B](03-picture-book.md) | Draw the broad read path using five boxes |
| 4 | Retell the read path | Inspect [one read](04-follow-a-read.md) | Find `Where`, `CountAsync`, and `ToListAsync`; say which execute work |
| 5 | Explain query versus result | Work [the paper cards](07-data-and-pages.md) | Predict one page and its total |
| 6 | Explain contact versus teammate again | Inspect [one write](05-follow-a-write.md) | Point to the starter call and the save |
| 7 | Explain prepare versus save | Translate [C# symbols](06-reading-csharp.md) | Read one actual statement as a plain sentence |
| 8 | Retell the invoice thread | Walk [state and time](08-state-and-time.md) | Predict a note's effect on state, first response, and activity |
| 9 | Explain what a workspace owns | Read [access checks](09-access-and-errors.md) | Explain the 403/404/401 example |
| 10 | Recall list versus overview | Follow [run and observe](10-run-and-observe.md) | Obtain a real workspace ID, list page, and overview |
| 11 | Predict a combined filter | Perform [one tiny test change](11-first-change.md) | Demonstrate a correct expectation, an intentionally wrong one, and the restored correct result |
| 12 | Choose two confusing ideas | Use [dialogues](12-pairing-dialogues.md) and [recall](13-repeat-and-recall.md) | Explain a new example without copying the original wording |

## Repeat with variation

When repeating contact versus teammate, change the names and customer problem.
When repeating paging, change the number of matching cards. When repeating
saves, ask about the opening message instead of the conversation object. The
underlying rule stays the same while the example changes.

For a second pass through the folder map, begin with a bug report: “Snoozing at
the current time should fail.” Ask where to look and why. For a third pass,
begin with an HTTP response and work backward to the action.

## Notes for a mentor

Ask one question and leave room for the answer. If the learner is stuck, reduce
the question: “Which value changed?” is easier to investigate than “Explain the
whole state machine.” Let them point to code, sketch, or speak first.

When correcting something, name the specific distinction: “You found where the
object is added. The save happens later; let's find that line.” Avoid labeling
the person or treating speed as the main measure.

Keep vocabulary consistent, then explicitly connect alternative wording:
“matching population” means “all the rows satisfying the filters.” Do not
introduce several unexplained synonyms at once.

If a test fails, ask what was expected and what was observed. Read the failure
together before proposing edits. Let the learner make and explain the smallest
change. End with a new example of the same principle.

## Reusable session note

```text
Today I followed this operation:
The source file and method were:
Before running it, I expected:
I observed:
In my own words, this means:
I want to revisit:
Next session I will check:
```

## Ready for a larger exercise?

Move on when you can follow one read and one write, explain the important
boundaries, run a focused test, and describe one limit of its evidence. You do
not need to memorize every endpoint.

Then use the [learning roadmap](../docs/learning/roadmap.md) and
[capstones](../docs/learning/capstones.md). Return here whenever a distinction
becomes fuzzy again.
