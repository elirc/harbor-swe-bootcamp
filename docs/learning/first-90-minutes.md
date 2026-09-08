# Your first 90 minutes

Goal: run Harbor, make an authenticated request, locate its implementation, and
change one test. Stop at each checkpoint and explain what happened aloud.

## 0–15 minutes: make the toolchain visible

From the repository root:

```powershell
dotnet --info
dotnet restore
dotnet build --no-restore
dotnet test --no-build --filter "FullyQualifiedName~ConversationTests"
```

You need the **.NET 10 SDK**, which includes the build tools. A runtime alone
cannot build the project. If `dotnet` is not found, check whether it is installed
and open a new terminal after installation. In the environment used for this
learning update, the SDK was installed at `C:\Users\E\.dotnet\dotnet.exe` without
changing the machine PATH; you can invoke that executable directly or add its
directory to PATH for the current terminal:

```powershell
$env:PATH = "C:\Users\E\.dotnet;" + $env:PATH
```

That path is specific to this workspace owner's machine. On another machine,
use your own SDK installation. Microsoft's [SDK installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
cover Windows setup.

`NuGet.Config` in the repository selects nuget.org for dependencies. If restore
fails, read the first error: a network/source failure differs from a missing SDK
or a C# compiler error. Do not randomly change package versions to hide it.

Checkpoint: explain the difference between restore, build, and test. Record the
SDK version and the actual result of your command.

## 15–35 minutes: send a request

Start the API in one terminal:

```powershell
dotnet run --project src/Harbor.Api
```

In a second PowerShell terminal:

```powershell
$baseUrl = 'http://localhost:5230'
$headers = @{ 'X-Api-Key' = 'hbk_dev_ada_admin' }
Invoke-RestMethod "$baseUrl/health"
$workspaces = @(Invoke-RestMethod "$baseUrl/api/workspaces" -Headers $headers)
$workspaceId = $workspaces[0].id
$rows = @(Invoke-RestMethod "$baseUrl/api/workspaces/$workspaceId/conversations?sort=OldestCreated&pageSize=2" -Headers $headers)
$rows | Select-Object id, subject, state, createdAt
Invoke-RestMethod "$baseUrl/api/workspaces/$workspaceId/conversations/overview" -Headers $headers
```

Development startup creates/migrates and seeds the local database. The seeded
key is a local demo credential. If there are no workspaces, first check the
environment and connection string described in [getting started](../getting-started.md).

The overview describes the **entire matching set**, so its total can exceed the
two rows you requested. Try `?state=Open` on both endpoints. Predict the overview's
`closed` value before sending the request.

Checkpoint: identify the HTTP method, path, query string, authentication header,
status, and body. These are different parts of the request/response contract.

## 35–55 minutes: trace one path

Open these in order:

1. [Program.cs](../../src/Harbor.Api/Program.cs): find controller registration and `ConversationQueries` registration.
2. [WorkspaceScopeFilter.cs](../../src/Harbor.Api/Infrastructure/WorkspaceScopeFilter.cs): find the workspace comparison.
3. [ConversationsController.cs](../../src/Harbor.Api/Controllers/ConversationsController.cs): find `List` and `Overview`.
4. [ConversationQueries.cs](../../src/Harbor.Api/Infrastructure/ConversationQueries.cs): find `State` filtering.
5. [Paging.cs](../../src/Harbor.Api/Infrastructure/Paging.cs): find `CountAsync`, `Skip`, and `Take`.

Draw five boxes and arrows. On each arrow, write the value being passed. Circle
the lines that actually execute a database query. A call to `Where` builds a
query; it does not fetch the rows at that point.

## 55–75 minutes: make a test say something useful

Read [ConversationQueueTests.cs](../../tests/Harbor.Tests/Integration/ConversationQueueTests.cs).
In the `Filters` table, add a case for `state=Closed&unassigned=true`. Predict the
expected ID list from the fixture before running it.

```powershell
dotnet test --filter "FullyQualifiedName~ConversationQueueTests"
```

Explain why an empty result is correct: the fixture's closed conversation is
assigned to a team. Then temporarily change your expected result to the closed
conversation's index. Run the test, inspect the assertion failure, and restore
the correct expectation. You have checked that the assertion can detect a wrong
result, not merely that the test runner finds your test.

## 75–90 minutes: teach it back

Write five sentences in your [journal](templates/learning-journal.md): what the
user requests, how Harbor checks workspace access, how SQL filters rows, why
paging happens afterward, and what your test protects.

Exit criteria: you can rerun your test, name the implementation it exercises,
and explain one thing it does **not** prove. For example, this test does not
measure performance under a million conversations. Next: [C# foundations](csharp-foundations.md).
