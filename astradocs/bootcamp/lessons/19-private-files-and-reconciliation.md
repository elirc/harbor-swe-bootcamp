# Lesson 19: Private files and reconciliation

S13 adds attachments to existing chat conversations. Read [PrivateFiles](../../../src/Harbor.Infrastructure/PrivateFiles.cs), [AttachmentsController](../../../src/Harbor.Api/Controllers/AttachmentsController.cs), [reconciliation](../../../src/Harbor.Infrastructure/AttachmentReconciliation.cs), and [attachment tests](../../../tests/Harbor.Tests/Integration/S13AttachmentTests.cs). Use [progress](../progress.md) for actual verification status.

## Start with the two things an upload creates

An upload creates file bytes on disk and metadata in SQL. These are two resources with different commit mechanisms. SQL can roll back a metadata insert; it cannot roll back a file rename that already happened.

This is why the feature has a lifecycle. Uploaded means a private staged file exists. Attached means a committed message owns it. Deleting means the system has durably decided to remove an unused upload and can retry file cleanup after interruption. Deleted metadata is removed after cleanup succeeds.

Imagine putting a parcel in a locked storeroom, then writing its inventory entry. If the inventory write fails, the parcel still exists. A later inventory reconciliation can remove an old unreferenced parcel. That analogy explains the need for cleanup; the real code still needs ownership checks, size limits, and safe paths.

## Paths are identifiers, not display names

A user may call a file `../proof.txt`. That name is useful only as a sanitized download label. It must never decide where the server writes bytes.

`PrivateFiles` builds paths from the configured private root, workspace ID, artifact kind, and a server-generated storage ID. It checks containment and refuses filesystem links at the managed locations. The storage ID is distinct from the public attachment ID. API responses expose filename, length, hash, state, and expiry; they omit filesystem paths and storage identity.

Trace one example: attachment U has storage ID S. The user sees U and `proof.txt`. The server resolves workspace W plus S to a private blob path. Renaming the display label cannot move the blob to another directory. A guessed ID from another workspace fails authorization before download.

Downloads use attachment disposition and `application/octet-stream`. The API is delivering file bytes, not rendering user-supplied HTML inside the application. The content hash identifies the uploaded bytes; it is not a statement that the file is trustworthy.

## Follow the upload boundary

Upload validates the scoped Chat conversation and enforces a 5 MiB limit. The file copier counts actual bytes while streaming, hashes them, flushes the completed temporary file, and renames it to an immutable blob ID. It does not hold a SQL writer reservation while receiving bytes.

Only after that does the API save Uploaded metadata. A failed or interrupted metadata commit leaves the blob private: the commit outcome can be uncertain, so deleting it immediately could remove content whose metadata actually committed. Reconciliation later removes blobs with no reference. A process crash can also leave an orphan between rename and metadata insertion; the same recovery path handles that gap.

Both normal and interrupted flows need bounds. Upload copying has a time budget. Temporary files and unreferenced blobs are eligible for cleanup only after 24 hours. A reconciliation call processes at most 100 artifacts. These limits are part of the feature, not tuning details to leave unspecified.

## Attaching is a different command

`AddMessageRequest.AttachmentIds` accepts up to three distinct IDs. The message command loads uploads for the authenticated uploader, workspace, and exact conversation. It requires Uploaded state, no existing MessageId, an unexpired staging window, and an existing blob.

It then stages the new message and marks those uploads Attached in one SQL transaction. Two competing messages cannot both claim the same staged upload. A failed message save leaves the upload staged and the message absent. Existing note/reply behavior, message activity, notifications, and outbox effects belong to that same commit.

Email conversations reject attachment use in this slice. Opening messages do not accept attachment IDs. State the supported scope precisely before designing a client.

## Rehearse four failures

| Failure | Durable result | Recovery |
| --- | --- | --- |
| Upload exceeds byte limit | No accepted metadata; temporary attempt removed | Send a smaller file |
| Crash after rename, before metadata commit | Private orphan blob | Reconcile after retention delay |
| Message commit fails | Upload remains staged | Retry the intended message after checking state |
| Attached metadata exists but blob is missing | Authorized download returns 503 | Operator investigates storage; do not serve an empty file |

The missing-blob test deliberately removes bytes after staging. It proves the server distinguishes “there is an attachment record” from “its content is available.” A successful response with an empty stream would hide a data-loss symptom.

Deletion is also two-stage. The uploader can delete a staged file. The system first commits Deleting, then removes bytes, then removes metadata. If it stops in the middle, another cleanup pass can continue. Attached files reject this deletion command with 409.

## Read tests as an executable ownership table

Write down uploader A, teammate B, conversations C1/C2, and workspaces W1/W2. Upload U as A for C1/W1. Predict each result before running the test:

- A attaches U to C1: accepted once.
- A attaches U to C2: 422.
- B attaches A's staged U: 422.
- B downloads staged U: 404.
- B reads U after it is attached to a conversation B may read: allowed.
- A tries a second claim or deletes an attached U: rejected.

Notice that uploader-only staging and conversation-scoped attached access are different authorization rules. Repeating the same check in both states would either leak staging or unnecessarily prevent colleagues from reading a saved message.

## Deployment and teach-back

Instances sharing one database must also share the configured durable private file store. Independent local directories would create valid metadata on one instance and missing downloads on another. Preserve storage and readers when changing deployment versions; deleting application binaries must not delete user attachments.

For your journal, record a file path only from a disposable test directory, the public attachment ID, state before/after, expected SQL commit, and observed remaining files. Keep real user file contents out of diagnostic notes.

Explain the feature three times: as uploading and sending evidence, as a metadata state machine, and as a crash timeline spanning disk and SQL. Finish by answering: which operation can SQL undo, which requires reconciliation, and why does an existing blob not automatically mean an attached message exists?
