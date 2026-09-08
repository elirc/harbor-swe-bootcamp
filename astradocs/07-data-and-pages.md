# 07: Understand data using paper cards

**Remember today:** filter the set, order the set, then take a page.

The letters below are imaginary paper-card labels, not valid API identifiers.
Keep this same small dataset in front of you for the whole page.

| Card | Workspace | State | Creation order | Assignment |
| --- | --- | --- | --- | --- |
| A | Acme | Open | 1 | Unassigned |
| B | Acme | Closed | 2 | Team |
| C | Acme | Open | 3 | Teammate |
| D | Acme | Open | 4 | Unassigned |
| E | Another company | Open | 5 | Unassigned |

## Explanation 1: move the cards by hand

Ask for Acme's open conversations, oldest created first, page size two.

First remove E because it belongs to another workspace. Remove B because it is
closed. You have A, C, and D. Their total is three. The first page is A and C.
The second page contains D. A page can contain fewer rows than the page size.

Notice that counting the first page gives two, while counting the matching set
gives three. Both numbers are meaningful, but they answer different questions.

## Explanation 2: use query words

| Paper action | Code idea |
| --- | --- |
| Keep only Acme | `Where` with the workspace ID |
| Keep only Open | `Where` with the state |
| Put oldest first | `OrderBy` creation time, then ID for ties |
| Count the remaining cards | `CountAsync` |
| Skip earlier pages and take this page | `Skip`, `Take`, `ToListAsync` |

See [ConversationQueries](../src/Harbor.Api/Infrastructure/ConversationQueries.cs)
for membership and ordering, then [Paging](../src/Harbor.Api/Infrastructure/Paging.cs)
for page execution.

## Explanation 3: describe the two HTTP answers

The list returns the selected page as a JSON array. Its `X-Total-Count` header
reports three matches for our paper example.

The overview returns an object for the whole filtered set:

```json
{ "total": 3, "open": 3, "snoozed": 0, "closed": 0, "unassigned": 2 }
```

A and D are unassigned. That count overlaps the open count. Do not add
`unassigned` to the state counts to calculate `total`.

Now remove the `state=Open` restriction. Acme's total becomes four, including B.
The foreign card E is still excluded. Tenant isolation applies to totals too.

## Explanation 4: distinguish a recipe from a meal

An `IQueryable` is like instructions for selecting data. `ToListAsync` is where
those instructions produce loaded rows. You can add conditions before executing
them. The analogy stops at execution details: databases plan and run SQL; they
do not literally walk C# objects like a cook following handwritten instructions.

Harbor's segment filter remains a database subquery. It does not first load every
matching contact ID into a giant list. A segment means a live group defined by
rules, such as contacts with a particular plan attribute.

## Two boundaries to practice

For creation dates, Harbor uses `createdFrom <= CreatedAt < createdBefore`.
If the window ends at midnight January 2, a conversation created exactly at that
midnight belongs in the next window. Do not invent the “last millisecond” of a day.

For pages, a very large page number should return an empty array. The helper
widens arithmetic before multiplying so an overflow cannot accidentally turn
the offset into a negative number.

## A limitation worth learning early

The cards stayed still during our example. A real queue can change between two
requests. A new conversation can shift numbered page boundaries even with a
unique sort order. An ID tie-breaker resolves equal timestamps; it does not
freeze the entire dataset.

## Try a variation

Using the paper cards, ask for `state=Open&unassigned=true`, oldest first, page
size one. Predict page one, total, and overview unassigned count.

<details>
<summary>Check your result</summary>

The set is A and D. Page one contains A. Total is two. The overview's unassigned
count is also two, because both matching conversations are unassigned.

</details>

Repeat: **membership, ordering, pagination**. In everyday words: **which ones,
in what order, how many this time**. Next: [state and time](08-state-and-time.md).
