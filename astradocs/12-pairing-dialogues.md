# 12: Think aloud with a patient teammate

**Remember today:** asking which part you do not understand is useful engineering work.

These are example conversations. Pause after the mentor's question and answer
before reading the next reply. Each conversation revisits something from an
earlier page using different words.

## Conversation A: “Which file am I supposed to read?”

**Learner:** There are so many files. Do I need to read all the controllers first?

**Mentor:** Choose one action a user performs. What did we just do in the terminal?

**Learner:** Asked for the conversation overview.

**Mentor:** Search for `Overview` in `ConversationsController`. What does that
method call to choose the conversations?

**Learner:** `queries.BuildAsync`.

**Mentor:** That is your next stop. We are following one operation, not reading
the repository from top to bottom. What question will you answer in that method?

**Learner:** Which conversations are included in this response?

**Mentor:** Exactly. Once you find the filter condition, return to the controller
and see how it counts the chosen set.

Try again alone: use the same approach for `List`. Where does the path differ?

## Conversation B: “I called Add, so it is saved?”

**Learner:** `db.Conversations.Add(conversation)` puts it in SQLite, right?

**Mentor:** It tells EF to insert that object when changes are saved. Which
statement marks the save in the controller?

**Learner:** `await db.SaveChangesAsync()`.

**Mentor:** Good. Think of `Add` as preparing an insert and `SaveChangesAsync`
as executing the pending database changes. Why might the starter leave saving
to its caller?

**Learner:** So the conversation and the related work can be saved together?

**Mentor:** Yes. The starter prepares the opening message, rules, and applicable
events. The caller controls the save. The exact guarantee comes from that database
transaction, not from the word `await`.

Try again alone: if response mapping succeeds, does that prove the save happened?
Explain why the mapping and persistence steps answer different questions.

## Conversation C: “The overview is wrong; I only got two rows.”

**Learner:** The list has two conversations but the overview says four.

**Mentor:** What page size did you request?

**Learner:** Two.

**Mentor:** Then two can be the page size and four the total matching population.
Which header lets you compare the list's total with the overview?

**Learner:** `X-Total-Count`.

**Mentor:** Right. Compare using the same filters, and remember separate requests
can observe changes. If the dataset stays still, the two totals should describe
the same matching set.

Try again alone: explain why counting the response array is insufficient for a
dashboard that needs counts for the entire filtered queue.

## Conversation D: “I keep mixing up contact and teammate.”

**Learner:** I remember it while reading, then forget later.

**Mentor:** Let's use the same people again. Maya asks about her invoice. Ada
works in support. Who is the contact?

**Learner:** Maya.

**Mentor:** And the teammate?

**Learner:** Ada.

**Mentor:** Now change the story: a customer called Jo asks Sam in support for
a refund. Give the roles without using Maya or Ada.

**Learner:** Jo is the contact. Sam is the teammate.

**Mentor:** Now find `AuthorContactId` and `AuthorTeammateId` in
[Message.cs](../src/Harbor.Domain/Entities/Message.cs). The code gives those
two identities different names because they represent different roles.

Try again alone: describe who may author an internal note in Harbor.

## Conversation E: “A green test means the feature is done?”

**Learner:** My filter test passes. Does that mean the feature is correct?

**Mentor:** What fixture and behavior did it check?

**Learner:** A Closed conversation with the billing tag appears in both reads.

**Mentor:** That is useful evidence. What did this one case not cover?

**Learner:** Invalid input, another tenant, and large-data performance.

**Mentor:** Good. Some existing cases cover the first two. Find them before
adding duplicate tests. Performance needs an appropriate measurement rather
than assuming a small fixture establishes it.

Try again alone: state one test's protection and one limit without using the
phrase “it tests everything.”

## A prompt for your own pairing session

> Help me understand one Harbor request. Ask one concrete question at a time.
> Wait for my explanation before adding a new concept. If I get stuck, restate
> it using the invoice example, then show the actual source. Revisit the idea
> with a different example after I understand it. Avoid giving the whole solution
> before I have tried one small step.

Next: [repeat and recall](13-repeat-and-recall.md).
