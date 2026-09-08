# 01: Start with the customer, not the folders

**Remember today:** Harbor stores customer-support conversations.

## Explanation 1: a short story

Maya has a question about an invoice. She sends a message to Acme Support.
Ada, a support teammate, reads the message, adds an internal note, sends a reply,
and eventually closes the conversation.

Harbor is the backend that stores those people, messages, and changes. This
repository does not contain a customer-facing chat screen. You interact with
the backend through HTTP requests and tests.

## Explanation 2: six things on a desk

Imagine six labeled cards. These labels match real code:

| Label | In the story | Meaning |
| --- | --- | --- |
| Workspace | Acme Support | The organization whose data this belongs to |
| Inbox | Billing support | A place inside the organization for conversations |
| Contact | Maya | The customer asking for help |
| Teammate | Ada | A person working in support |
| Conversation | The invoice question | The thread and its current state |
| Message | “Why was I charged twice?” | One reply or internal note in the thread |

A conversation is the container for a thread. A message is one entry in it.
A contact is the customer. A teammate is the support person. Those are separate
types because the application treats their actions differently.

## Explanation 3: one sentence with blanks

“A **contact** sends a **message** in a **conversation**, inside an **inbox**,
belonging to a **workspace**. A **teammate** works on that conversation.”

Read that sentence twice. On the second reading, replace each bold word with
Maya, the invoice message, the invoice thread, Billing, Acme, and Ada.

## Find one real file

Open [Conversation.cs](../src/Harbor.Domain/Entities/Conversation.cs).
Find `WorkspaceId`, `InboxId`, and `ContactId` near the top.

Each ends in `Id` because it identifies another record. It is a reference to
the workspace, inbox, or contact. You do not need to understand how the database
enforces those connections yet.

Find `State`. A new conversation starts as `Open`. Later it can be `Snoozed`
or `Closed`. Closing does not delete the stored conversation.

## Try saying it back

Is Maya the teammate? Is the invoice thread one message? Does closing erase it?

<details>
<summary>Check your explanation</summary>

Maya is the contact. Ada is the teammate. The conversation holds the thread;
it can contain several messages. Closing changes the state and timestamps;
it does not delete the conversation.

</details>

## Say the same idea another way

Harbor remembers **who asked**, **what was said**, and **what is happening with
the request for help**. The folders contain code for doing that reliably.

Stop here if that is enough for today. Next: [the folder map](02-folder-map.md).
