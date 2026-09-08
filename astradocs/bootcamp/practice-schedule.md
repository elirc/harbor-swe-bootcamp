# A repeatable practice schedule

Progress is demonstrated by what you can explain and change, not by how quickly
you finish a document. Repeating an exercise with a different dataset is useful
practice. You do not need to memorize every controller.

## A 30-minute session

| Minutes | Activity | Concrete output |
| --- | --- | --- |
| 0-5 | Recall yesterday without opening the source | Three sentences and one uncertainty |
| 5-12 | Trace one request through the current lesson | File names and responsibilities in your own words |
| 12-20 | Predict and run a focused test | Expected result, actual result, explanation of differences |
| 20-27 | Change one fixture or try the lesson's debugging experiment | One observed failure and its cause |
| 27-30 | Restore experiments and write a teach-back | One rule you understand and one question to revisit |

Use a separate practice branch or a clean copy for deliberate breakage. Check
your diff before restoring anything; do not discard unrelated changes.

## Revisit each lesson in different ways

| When | Prompt | Why the form changes |
| --- | --- | --- |
| First session | Follow the HTTP request and point to each file | Establish where behavior lives |
| Next day | Predict results for a new four-row dataset | Check the rule beyond the original example |
| Three days later | Explain one failure without source access | Retrieve the reason rather than recognize familiar text |
| One week later | Review a deliberately incorrect change | Practice the judgment used in code review |
| Two weeks later | Implement a similar small filter independently | Transfer the skill to a new requirement |

For contact presence, use a shipping address instead of email in your verbal
example. For pagination, use library books. For tenant ownership, use two
companies sharing one application. Always map the analogy back to the actual
predicate and response before ending the exercise.

## Your journal template

Copy this into your own dated entry. The project's [implementation journal](journal.md)
records the build work; this template records what **you** learned.

```text
Date and lesson:
User problem in my own words:
My prediction before running anything:
Request -> validation -> query/write -> response:
Test command and actual result:
What surprised me:
One incorrect implementation this test would catch:
One behavior this test does not prove:
My two-minute explanation:
Question for the next session:
```

## Readiness checks

You can move beyond basic filters when you can distinguish absent, false, and
invalid input; put workspace scope around an OR search; and state the full set
behind a paged count. You can start private-resource features when you can also
explain why two people in the same workspace might still be forbidden from
reading each other's drafts.

For concurrency work, be able to narrate two requests interleaving: both read,
one writes, the other attempts a stale write. For background work, narrate a
process stopping after each persistence boundary and identify what the next
process can recover. The later lessons should build these explanations on the
same small, concrete traces you practiced with filters.

When stuck, reduce the example. Use two rows, two requests, or one boundary
timestamp. Draw the expected result before adding more code. Record the exact
uncertainty instead of labelling yourself as bad at the topic.
