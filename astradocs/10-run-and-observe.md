# 10: Run the app and observe one thing at a time

**Remember today:** a successful command should answer a specific question.

These commands use PowerShell and start from the repository root, the directory
containing `HarborNet.slnx`. They inspect the Development demo. Actual IDs and
counts come from your database; do not expect the paper-card examples' values.

## Step 1: can this terminal find the SDK?

```powershell
dotnet --version
```

You need a .NET 10 SDK. If it is installed in this workspace owner's local
directory but not on PATH, this changes PATH only for the current terminal:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
dotnet --version
```

If that directory does not contain an SDK, follow the prerequisite links in the
[existing setup guide](../docs/learning/first-90-minutes.md). A missing command
is a setup issue; it does not yet say anything about the C# code.

## Step 2: can the repository build?

```powershell
dotnet restore
dotnet build --no-restore
```

Restore obtains package dependencies using `NuGet.Config`. Build compiles the
projects. If a step fails, read its first error before proceeding. Copy the
command and error into your notes; do not change package versions at random.

## Step 3: start the API in terminal A

```powershell
dotnet run --project src/Harbor.Api --no-build --launch-profile http
```

Wait until the console says the application is listening on port 5230. In
Development, startup applies database migrations and seeds demo data. Leave this
terminal running. It is the server, not a command that should immediately finish.

If port 5230 is occupied, use an unused port explicitly:

```powershell
dotnet run --project src/Harbor.Api --no-build --launch-profile http -- --urls http://localhost:5231
```

Use the matching port in `$baseUrl` below. Do not stop an unknown process merely
because it is using the original port.

## Step 4: ask a health question in terminal B

```powershell
$baseUrl = 'http://localhost:5230'
Invoke-RestMethod "$baseUrl/health"
```

This checks that the host responds and its health endpoint can probe the
database. It does not prove that every feature works or that webhooks are being sent.

## Step 5: identify the demo workspace

```powershell
$headers = @{ 'X-Api-Key' = 'hbk_dev_ada_admin' }
$workspaces = @(Invoke-RestMethod "$baseUrl/api/workspaces" -Headers $headers)
if ($workspaces.Count -eq 0) { throw 'No accessible workspace; check Development seeding.' }
$workspaceId = $workspaces[0].id
$workspaces | Select-Object id, name
```

`@(...)` makes the result convenient to treat as an array. `[0]` means its first
item. The well-known key is a Development demo key; real deployments use their
own credentials. Do not paste real keys into learning notes or screenshots.

## Step 6: see a page and then the entire queue's counts

```powershell
$route = "$baseUrl/api/workspaces/$workspaceId/conversations"
$page = @(Invoke-RestMethod "${route}?sort=OldestCreated&pageSize=2" -Headers $headers)
$page | Select-Object id, subject, state, createdAt
$overview = Invoke-RestMethod "$route/overview" -Headers $headers
$overview
```

Predict before running: can `overview.total` exceed `$page.Count`? Yes. The page
is bounded to two rows while the overview counts the entire filtered population.
It is also possible to have fewer than two rows if your dataset is small.

The braces in `${route}?` tell PowerShell exactly where the variable name ends.
This is shell syntax, not part of Harbor's API.

## Step 7: change only one filter

```powershell
Invoke-RestMethod "$route/overview?state=Open" -Headers $headers
```

The snoozed and closed counts should be zero because every count uses the Open
filter. If there are no open matches, all counts are zero. Write one sentence
explaining why the unassigned count can overlap the open count.

## Step 8: locate the code you just used

Open [ConversationsController.cs](../src/Harbor.Api/Controllers/ConversationsController.cs).
Find `Overview`. Then find its call to `queries.BuildAsync`. You now have a real
response connected to a real source path.

When finished, press Ctrl+C in terminal A to stop the server you started. Your
local demo database remains on disk. You do not need to delete it to reread the docs.

## If something surprises you

| Observation | First thing to check |
| --- | --- |
| Connection refused | Is your server running, and are the ports the same? |
| 401 | Did you send the demo key to the Development demo database? |
| 403 | Does the route workspace match the key's workspace? |
| Empty list | Which filters did you apply, and are there matching rows? |
| Response took a while | Is startup complete? Record elapsed behavior before assuming a logic bug |

Next: [make one tiny test change](11-first-change.md).
