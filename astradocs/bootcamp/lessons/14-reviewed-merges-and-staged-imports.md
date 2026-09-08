# 14: Review a plan before changing live data

Stories: S06-S07. These features are being integrated; use [progress](../progress.md) and [the journal](../journal.md) for executed verification.

## Two operations with a shared shape

A merge consolidates two existing contacts. An import creates several new contacts. Both benefit from separating inspection from mutation: prepare a durable plan or batch, show its meaning, and commit it under explicit rules.

The two states solve different problems. A merge preview can become stale because existing contacts and conversations continue changing. An import batch contains immutable staged input, so its main questions are validity, expiry, and whether it already committed. Do not give every planned command the same conflict rules merely because they share a POST endpoint.

## Draw the contact reference graph

Draw Source and Target contacts. Connect Source to conversation C1 through ContactId. Connect customer message M1 to Source through AuthorContactId. Connect teammate note M2 to a teammate through AuthorTeammateId.

After merging Source into Target, C1 and M1 must point to Target. M2 must still point to its teammate. Bodies, timestamps, channels, and message identities stay intact. Removing Source before moving both live contact references would either fail a foreign-key constraint or destroy the meaning of the history.

Now draw a saved email receipt containing Source's old ID. That receipt describes a historical result, not a current ownership relationship. S06 leaves it unchanged. Rewriting immutable event payloads and receipts would pretend that the original operation returned a different identity.

Read [ContactMergePreview.cs](../../../src/Harbor.Infrastructure/ContactMergePreview.cs). It inventories current foreign keys pointing to Contact and refuses an unrecognized live dependency. Adding a new contact-owned feature later must include a deliberate merge policy; it should not silently disappear through a cascade.

## The field policy is part of the product

The target keeps its name and email. If its external ID is missing, it can inherit the source's nonblank external ID. Conflicting nonblank external IDs make the merge ineligible. Attributes are combined with target values winning conflicts, and LastSeenAt becomes the later nonnull value.

Try this fixture: Source has `plan=free` and `seats=10`; Target has `plan=pro`. The result is `plan=pro,seats=10`. The preview lists the conflicting plan values so the administrator can see what will be retained. A merge that silently chooses a winner is harder to review than one that states the policy and shows its consequences.

Eligibility is deliberately narrow: distinct contacts in one workspace, the same nonblank normalized email, no conflicting external IDs, at most 100 source conversations, and at most 1,000 source-authored messages. The final attribute map must fit its stored limit. These bounds limit the synchronous transaction; they do not prove that matching email establishes a person's identity.

## A preview needs a freshness check

The preview stores a schema version, fingerprint, field decisions, expiry, and creator. Its fingerprint includes both contacts' state and versions plus affected relationship identities/versions. The commit action recomputes that fingerprint using the same helper under an immediate write transaction.

Read [ContactMergesController.cs](../../../src/Harbor.Api/Controllers/ContactMergesController.cs) as a decision tree:

1. Find the plan in the caller's workspace; only administrators enter this controller.
2. If already committed, return the original saved result.
3. If its ten-minute window ended, return 410.
4. Rebuild the preview. Changed eligibility or fingerprint returns 409 and asks for a new review.
5. Repoint conversations and customer-message authors, apply target field policy, and remove Source.
6. Save the result and committing administrator with the data changes, in one transaction.

A different administrator in the same workspace may commit the plan. Creator and committer are recorded separately. The request does not trust a client-supplied fingerprint as permission to mutate; it loads the persisted plan and recomputes the current facts.

Explain this another way: a preview is a promise conditioned on facts remaining the same. The transaction reserves the writer while those facts are checked and acted on. A new customer reply between preview and commit changes the facts, so the administrator must review again.

## Import staging does not create contacts

Read [ContactImportsController.cs](../../../src/Harbor.Api/Controllers/ContactImportsController.cs). The request contains 1-200 JSON rows and is capped at 3 MiB. Each row is validated independently; the response is a durable Ready or Invalid batch with 1-based row numbers and errors.

If rows 1 and 2 are valid and row 3 has an invalid email, the batch is Invalid and there are zero new live contacts. Commit does not quietly import rows 1 and 2. Correcting input means staging another immutable batch.

The row payload uses JsonElement deliberately. If the outer contract directly nested the ordinary validated contact DTO, MVC could reject the entire request at the first invalid row before the staging action could produce its row-by-row report. The action instead parses each row and runs validation explicitly, retaining safe field errors.

## A subtle record-validation boundary

Read [ContactImportValidation.cs](../../../src/Harbor.Api/Infrastructure/ContactImportValidation.cs) and [ContactImportValidationTests.cs](../../../tests/Harbor.Tests/Unit/ContactImportValidationTests.cs). CreateContactRequest is a positional record whose validation attributes live on constructor parameters. Calling TryValidateObject on the record alone would not necessarily read the same metadata that MVC uses.

The validator reads those parameter attributes and validates their corresponding property values. It also checks normalized attribute-key collisions, key/value bounds, and final serialized size. Name normalization matches ordinary contact creation: trim the outside while retaining internal spaces; email and external ID preserve their existing create semantics.

This is a useful debugging habit: when two paths supposedly share validation but disagree, inspect where the metadata is attached. A familiar validation function is not proof that it sees the same contract.

## Commit once, return the same mapping

Contact import commit reserves the writer, loads the batch and ordered rows, and first checks whether it already committed. A completed batch returns its original contact IDs. An expired uncommitted batch returns 410; an Invalid or unsupported batch cannot commit.

For a Ready batch, construct contacts in row order, save their IDs on the staged rows, mark the batch Committed, and save once. Duplicate emails and external IDs remain allowed, matching the current create-only contact model. Batch identity deduplicates commit; staging the same people twice creates two different batches and does not deduplicate people globally.

Draw rows 1/2/3 pointing to C1/C2/C3 after commit. Lose the response and repeat. The arrows must still point to C1/C2/C3. Without saved row mappings, the server could not distinguish replaying a completed batch from creating three more contacts.

## Rehearse failures and explain the evidence

Read [S06ContactMergeTests.cs](../../../tests/Harbor.Tests/Integration/S06ContactMergeTests.cs) and [S07ContactImportTests.cs](../../../tests/Harbor.Tests/Integration/S07ContactImportTests.cs).

Predict the result of a new source reply after merge preview; changing Target's name after preview; committing exactly at expiry; importing one malformed JSON row among valid rows; and committing a Ready batch twice. For each, name the HTTP status, surviving contacts, and saved plan/batch state.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~S06|FullyQualifiedName~S07|FullyQualifiedName~ContactImportValidationTests|FullyQualifiedName~MigrationDriftTests'
```

Test names and fixtures are reading aids, not a claim that the current implementation has passed. Check the journal's actual run results. A shared transaction-helper test is useful evidence but does not replace a failure test that interrupts the specific merge/import write sequence.

Tomorrow, explain the difference between a stale plan and invalid input. Then draw the reference graph again from memory and identify which historical IDs stay unchanged. Those explanations are the core skill: making a data operation concrete enough to review before it changes live records.
