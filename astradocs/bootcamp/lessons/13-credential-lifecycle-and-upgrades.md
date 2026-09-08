# 13: A credential is not a teammate

Story: S05. Code and tests are staged; consult [progress](../progress.md) for migration and verification status.

## Start with a rotation problem

One teammate uses Harbor from a laptop and a relay client. If both share one key, replacing it affects both clients at once. Separate named credentials let the teammate create a replacement, move one client, and revoke the unused key while other clients keep working.

The teammate remains the identity that owns a workspace role. A credential is one means of authenticating as that teammate. Moving key state into [TeammateApiKey.cs](../../../src/Harbor.Domain/Entities/TeammateApiKey.cs) expresses that one-to-many relationship directly.

Each credential stores its hash, owner/workspace IDs, name, creation time, optional expiry, and optional revocation time. A new generated key is returned only once. Listing credentials returns metadata, not raw keys or hashes. Hashing is one-way: losing the create response means the raw value cannot be recovered from the database.

## Trace every creation path

Before changing authentication, search for `ApiKeyHash` and `ApiKeys` across source. The relevant production writers are workspace bootstrap, teammate creation, and the development seeder. Updating only the new lifecycle controller would leave newly bootstrapped workspaces unable to authenticate after cutover.

Read `StageInitial` in [ApiKeys.cs](../../../src/Harbor.Infrastructure/ApiKeys.cs) and its callers in [WorkspacesController.cs](../../../src/Harbor.Api/Controllers/WorkspacesController.cs), [TeammatesController.cs](../../../src/Harbor.Api/Controllers/TeammatesController.cs), and [DataSeeder.cs](../../../src/Harbor.Infrastructure/DataSeeder.cs). Each creates the initial credential in the same save as the new teammate. Initial compatibility credentials are nonexpiring until revoked.

The legacy hash column remains for schema compatibility during this release. Keeping a column is not the same as continuing to trust it. After cutover, authentication reads usable credential rows only. Falling back to the legacy column would allow a revoked initial credential to authenticate again.

## Follow authentication from header to claims

Read [ApiKeyAuthenticationHandler.cs](../../../src/Harbor.Api/Infrastructure/ApiKeyAuthenticationHandler.cs).

1. Read the API-key header and hash its supplied value.
2. Capture the evaluation time once through the injected clock.
3. Look up a credential with that hash, no revocation, and no expiry or an expiry strictly after now.
4. Join its current teammate and verify matching workspace ownership.
5. Build claims using the teammate's current ID, name, role, workspace, and the credential ID.

At exactly ExpiresAt, authentication fails. A role is not copied into the key when it is created; changing the teammate's role affects the next request from every key. Already-running requests have already authenticated, so revocation is enforced on subsequent authentication rather than retroactively interrupting arbitrary application code.

Explain this using keys and a staff directory: the key opens the door only while valid, and the directory determines what the person can do after entry. Reissuing every key just to reflect a role change would be unnecessary and error-prone.

## Enforce a limit against competing requests

Read [ApiKeysController.cs](../../../src/Harbor.Api/Controllers/ApiKeysController.cs). A teammate manages their own keys; an administrator may manage teammates in the same workspace. Foreign resources are unavailable. An ordinary teammate cannot list or revoke another person's credentials.

Creation requires a trimmed nonblank name of at most 100 characters and a lifetime of 15 minutes through 30 days. There can be at most five currently usable credentials. Expired and revoked rows remain visible as metadata but no longer consume usable slots.

Count and insert occur under the shared immediate write transaction. If two requests both see four usable keys without this serialization, both could insert and produce six. The limit belongs to the committed state, not merely to a validation check that was true a moment earlier.

The S05 scenario in [IngressConcurrencyTests.cs](../../../tests/Harbor.Tests/Integration/IngressConcurrencyTests.cs) arranges four usable keys, releases two HTTP creation requests against an isolated SQLite file, and checks that exactly one new key succeeds and the persisted total is five. A 503 from bounded writer contention is acceptable; a sixth credential is not.

Revocation sets a timestamp only when it is absent. Repeating revocation with another usable credential returns 204 and preserves the original timestamp. Revoking the caller's current credential is allowed; that request succeeds, and its next request receives 401.

## Upgrade without knowing the raw secrets

Existing databases contain hashes on teammate rows. The migration copies each existing hash into one nonexpiring credential with a deterministic initial ID. It does not need to reverse the hash. This is an important migration pattern: preserve an existing trusted representation while changing where it is stored.

Read [CredentialUpgradeTests.cs](../../../tests/Harbor.Tests/Unit/CredentialUpgradeTests.cs). It builds the previous schema, inserts a known hashed credential through old columns, upgrades, verifies the copied identity/hash, revokes it, and reruns seeding/migration. Neither repeated seeding nor migration may resurrect the revoked credential.

The new table's unique hash index prevents ambiguous authentication. Ownership indexes support lifecycle queries. The API response explicitly projects safe metadata so exposing the entity later does not accidentally expose hashes.

## Practice the whole operational flow

Read [S05CredentialLifecycleTests.cs](../../../tests/Harbor.Tests/Integration/S05CredentialLifecycleTests.cs). Predict the status of each request in this sequence: A works; create B; B works; revoke A using B; A fails; B works; revoke B using B; B's next request fails. Identify which remaining administrator credential can recover access afterward.

```powershell
dotnet test tests/Harbor.Tests/Harbor.Tests.csproj --filter 'FullyQualifiedName~S05|FullyQualifiedName~CredentialUpgradeTests|FullyQualifiedName~Authorization|FullyQualifiedName~SeedDataSmokeTests|FullyQualifiedName~MigrationDriftTests'
```

Now rehearse a lost create response. The database contains B's hash, but the client never received B. Using A, list metadata, revoke unused B, and create C. Do not add credential responses to the generic conversation receipt mechanism: retained raw keys would violate the one-time return contract.

For tomorrow's exercise, draw the old and new authentication queries beside one another. Cross out the old query after cutover. Explain why rolling back to that query can re-enable a revoked key. This migration needs coordinated deployment or a forward fix; retaining the old column alone does not make an old authenticator safe.

The readiness test is being able to explain the full path—generation, hashing, transactional insertion, authentication, expiry, revocation, upgrade, and recovery—using one teammate with two clients. That is feature ownership beyond adding a POST endpoint.
