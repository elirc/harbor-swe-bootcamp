# AstraDocs: get comfortable with Harbor

Start with [the five-minute introduction](01-start-here.md). You only need to
read that page for your first session.

These guides explain the codebase in small steps. They deliberately revisit the
same ideas through stories, pictures, code, conversations, and practice. You can
reread a page or choose a different explanation without completing everything
before it. There is no speed requirement.

For the ongoing feature-building curriculum, open the
[implementation bootcamp](bootcamp/README.md). Its
[75-story tracker](bootcamp/progress.md) distinguishes planned work from verified
features, and its [working journal](bootcamp/journal.md) records decisions and
test evidence alongside the lessons.

## Three ideas we will keep returning to

1. **Follow one request.** Someone asks the API to read or change something.
2. **Name each responsibility.** The API handles HTTP, the domain expresses
   business behavior, and infrastructure handles persistence and integrations.
3. **Check the result.** A test compares the observed behavior with an expectation.

You do not need to memorize the wording. You will meet these ideas repeatedly
with the same example: a customer asks for help with an invoice.

## Pick the explanation you need today

| I want to… | Open this | A small stopping point |
| --- | --- | --- |
| Understand what this application does | [01: Start here](01-start-here.md) | Explain contact versus teammate |
| Know which folder to open | [02: The folder map](02-folder-map.md) | Find the conversation entity |
| See the whole picture | [03: Picture book](03-picture-book.md) | Follow one arrow at a time |
| Understand a GET request | [04: Read one conversation list](04-follow-a-read.md) | Explain where rows are fetched |
| Understand a POST request | [05: Start one conversation](05-follow-a-write.md) | Find where changes are saved |
| Decode unfamiliar C# | [06: Read the symbols](06-reading-csharp.md) | Translate one line into a sentence |
| Understand filtering, SQL, and pages | [07: Data with paper cards](07-data-and-pages.md) | Predict the page and total |
| Understand states and timestamps | [08: A conversation over time](08-state-and-time.md) | Explain what a note changes |
| Understand access checks and errors | [09: Who can do what](09-access-and-errors.md) | Distinguish 401, 403, and 404 |
| Run the app without inventing commands | [10: Run and observe](10-run-and-observe.md) | Get a successful overview response |
| Make my first small contribution | [11: One tiny test change](11-first-change.md) | Add one expected filter result |
| See a patient pairing conversation | [12: Think aloud together](12-pairing-dialogues.md) | Answer before reading the mentor reply |
| Revisit things I keep mixing up | [13: Repeat and recall](13-repeat-and-recall.md) | Explain one idea from memory |
| Plan short onboarding sessions | [14: Onboarding sessions](14-onboarding-sessions.md) | Complete one session's observable task |
| Find a feature or translate a word | [15: Desk reference](15-desk-reference.md) | Locate a source file and its test |

If a diagram does not render in your reader, use the words immediately beneath
it. Every diagram has a text explanation.

## How each short session works

Read one section. Find one matching file. Predict one result. Check it. Explain
what happened in your own words. Then stop, or continue if you have energy.

The answer boxes are there to help, not to grade you. Try answering first; if
you need to look, close the answer and explain it again with a different example.

## How this fits the other documentation

AstraDocs is the slower introduction with repeated explanations. The existing
[learning curriculum](../docs/learning/README.md) extends into larger exercises,
design decisions, and mid/senior engineering practice. The
[API reference](../docs/api-reference.md) lists the detailed contract. Start with
understanding one path before using those as reference material.

The examples describe the implementation in this checkout. Imaginary sample IDs
such as “A” and “B” are labeled as paper examples, not values to paste into API
requests. The runnable guide obtains real IDs from your local application.
