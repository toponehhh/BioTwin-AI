# Confirmed Resume Import Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an API-owned, cancellable resume import pipeline that starts only after confirmation, bypasses conversion for Markdown, rejects concurrent user sessions, and prevents stale clients from overwriting newer resume state.

**Architecture:** `IResumeOperationCoordinator` provides one active create/import session per user, with a singleton memory implementation that can later be replaced by Redis. An opaque token computed from current resume rows provides optimistic concurrency for acquire and save without schema changes. A singleton import-job coordinator uses scoped API services to run inspection, conversion, merge, and extraction, while Blazor only uploads, polls, displays API progress, and sends cancel/retry commands.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core 10 with SQLite, Microsoft.Extensions.AI, Blazor WebAssembly, xUnit.

## Global Constraints

- The Blazor client calls only BioTwin API endpoints and never sees provider URLs, names, job IDs, messages, or errors.
- Selecting files makes no API request; creation starts on Continue and workspace import starts on Import.
- API startup validates schema but never creates or alters schema or seed data.
- This feature does not change database schema or migration scripts.
- One user can hold only one active resume creation/import lease within the current API process.
- All resume writes require a current operation lease and expected resume-state token.
- The coordinator abstraction must remain replaceable by a future Redis implementation.
- Do not commit or push. The user explicitly controls commits.
- After verification, stop every API, client, conversion, and preview service started during implementation.

---

### Task 1: Opaque Resume-State Token

**Files:**
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeStateDto.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeStateTokenService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeStateTokenService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeStateTokenServiceTests.cs`

**Interfaces:**
- Produces: `ComputeAsync(string tenantId, CancellationToken)` returning a Base64Url SHA-256 token.
- Produces: `GET /api/resumes/state` returning `ResumeStateDto(string Token, IReadOnlyList<ResumeSummaryDto> Resumes)`.
- Consumes only existing `ResumeEntries`; no schema changes.

- [x] **Step 1: Write failing deterministic-token tests**

```csharp
var emptyToken = await service.ComputeAsync("huangd", CancellationToken.None);
context.ResumeEntries.Add(CreateResume("huangd", "en", updatedAt));
await context.SaveChangesAsync();
var changedToken = await service.ComputeAsync("huangd", CancellationToken.None);
Assert.NotEqual(emptyToken, changedToken);
Assert.Equal(changedToken, await service.ComputeAsync("huangd", CancellationToken.None));
```

Also assert insertion order does not affect the token and changes to ID, language, `UpdatedAt`, or source-file hash do affect it.

- [x] **Step 2: Run token tests and verify RED**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeStateTokenServiceTests" --verbosity minimal
```

Expected: FAIL because the token service does not exist.

- [x] **Step 3: Implement canonical token generation**

Query no-tracking resume rows ordered by `Language`, then `Id`. Serialize only `Id`, normalized language, `UpdatedAt.UtcTicks`, and normalized source hash with unambiguous length prefixes. Hash UTF-8 bytes with SHA-256 and encode Base64Url without padding.

```csharp
public interface IResumeStateTokenService
{
    Task<string> ComputeAsync(string tenantId, CancellationToken cancellationToken);
}
```

- [x] **Step 4: Add the state endpoint and run tests GREEN**

Return the token together with the existing summaries. Run the command from Step 2. Expected: all token tests pass.

---

### Task 2: Replaceable In-Memory Resume Operation Coordinator

**Files:**
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeOperationDtos.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeOperationCoordinator.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/MemoryResumeOperationCoordinator.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeOperationService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeOperationService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeOperationConflictException.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumeOperationsController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeOperationServiceTests.cs`

**Interfaces:**
- Produces: `AcquireAsync(int userId, string tenantId, string operationType, string expectedStateToken, CancellationToken)`.
- Produces: `HeartbeatAsync(int userId, string operationId, CancellationToken)`.
- Produces: `ReleaseAsync(int userId, string operationId, CancellationToken)`.
- Produces: `ValidateAsync(int userId, string operationId, string expectedStateToken, CancellationToken)`.
- Produces endpoints `POST`, `PUT heartbeat`, and `DELETE` under `/api/resumes/operations`.

- [x] **Step 1: Write failing lease tests**

Cover acquire, same-user conflict, different-user success, stale state token, heartbeat renewal, explicit release, and expired lease replacement.

```csharp
var first = await service.AcquireAsync(user.Id, "huangd", "import", currentToken, CancellationToken.None);
var conflict = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
    service.AcquireAsync(user.Id, "huangd", "create", currentToken, CancellationToken.None));
Assert.Equal("resume_operation_in_progress", conflict.Code);
```

- [x] **Step 2: Run lease tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeOperationServiceTests" --verbosity minimal
```

Expected: FAIL because the coordinator, service, and contracts do not exist.

- [x] **Step 3: Implement the replaceable coordinator abstraction**

Use an async abstraction whose semantics can be implemented with Redis `SET NX` later:

```csharp
public interface IResumeOperationCoordinator
{
    ValueTask<ResumeOperationLeaseDto?> TryAcquireAsync(int userId, string operationType, string stateToken, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    ValueTask<ResumeOperationLeaseDto?> RenewAsync(int userId, string operationId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    ValueTask<bool> ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken);
    ValueTask<ResumeOperationLeaseDto?> GetAsync(int userId, CancellationToken cancellationToken);
}
```

Implement it with `ConcurrentDictionary<int, LeaseState>` and per-user atomic `TryAdd`/`TryUpdate` loops. Expired entries may be replaced; active entries may not. Never expose mutable internal lease state.

- [x] **Step 4: Implement state validation and API conflicts**

`ResumeOperationService.AcquireAsync` computes the current token before calling the coordinator and rejects a mismatch with `resume_state_stale`. A failed `TryAcquireAsync` translates to:

```csharp
throw new ResumeOperationConflictException(
    "resume_operation_in_progress",
    "A resume is already being created or imported.",
    "Return to the other client, or wait for its session to expire.");
```

Use a two-minute lease and renew it to `TimeProvider.GetUtcNow().AddMinutes(2)`.

```csharp
public sealed record AcquireResumeOperationRequest(string OperationType, string ExpectedStateToken);
public sealed record ResumeOperationLeaseDto(string OperationId, string ResumeStateToken, DateTimeOffset ExpiresAt);
public sealed record ResumeApiErrorDto(string Code, string Message, string RecoveryHint, bool CanRetry);
```

Return `409 Conflict` for `resume_operation_in_progress` and `resume_state_stale`; return `404` for another user's operation ID.

- [x] **Step 5: Register memory coordinator and run tests GREEN**

Register `IResumeOperationCoordinator` as singleton and `IResumeOperationService` as scoped. Run the command from Step 2. Expected: all lease and stale-token tests pass.

---

### Task 3: Enforce Fresh Resume State on Every Mutation

**Files:**
- Modify: `src/BioTwin_AI.DotNetShared/Resumes/SaveResumeMarkdownRequest.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeLanguageWorkspaceTests.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeStateConcurrencyTests.cs`

**Interfaces:**
- Consumes: `GET /api/resumes/state` from Task 1.
- Changes save requests to carry `OperationId` and `ExpectedStateToken`.
- Successful save, replace, or delete produces a different token because persisted resume state changes.

- [x] **Step 1: Write failing stale-write tests**

```csharp
await operationService.AcquireAsync(user.Id, tenant, "create", currentToken, CancellationToken.None);
var exception = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
    resumeService.SaveAsync(tenant, request with { ExpectedStateToken = staleToken }, user.Id, CancellationToken.None));
Assert.Equal("resume_state_stale", exception.Code);
```

Also assert a successful mutation changes the token and a stale client cannot replace the canonical same-language resume.

- [x] **Step 2: Run concurrency tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeStateConcurrencyTests" --verbosity minimal
```

Expected: FAIL because save ignores the operation lease and expected state token.

- [x] **Step 3: Add operation and expected-token fields**

```csharp
public sealed record SaveResumeMarkdownRequest(
    string Title,
    string Markdown,
    string Language,
    string? SourceFileName,
    string? SourceContentType,
    long? SourceFileSize,
    string? SourceFileContentBase64 = null,
    string? OperationId = null,
    string? ExpectedStateToken = null);
```

- [x] **Step 4: Make mutations transactional and token-validated**

Validate the lease and recompute the state token inside the write transaction before querying or replacing a canonical resume. After all resume sections and vectors save, return the newly computed token. Preserve the `(TenantId, Language)` unique index and translate a unique-key race to `resume_state_stale` rather than overwriting.

- [x] **Step 5: Run concurrency and existing language tests**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeStateConcurrencyTests|FullyQualifiedName~ResumeLanguageWorkspaceTests" --verbosity minimal
```

Expected: all selected tests pass.

---

### Task 4: API-Owned Import Job Pipeline

**Files:**
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeImportJobDto.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeImportJobService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeImportJobService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeImportJobProcessor.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeConversionJobService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeImportJobServiceTests.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeConversionJobServiceTests.cs`

**Interfaces:**
- Produces: `POST /api/resumes/import-jobs/wizard` and `/workspace`.
- Produces: `GET /api/resumes/import-jobs/{jobId}`.
- Produces: `DELETE /api/resumes/import-jobs/{jobId}`.
- Consumes: a valid operation ID and expected resume-state token.

- [x] **Step 1: Write failing Markdown bypass and API-orchestration tests**

Assert that `.md` and `.markdown` inputs never call the external HTTP handler, that wizard mode invokes merge and extraction inside the API, and that workspace mode returns draft results without client-side processing.

```csharp
var job = await service.StartWizardAsync(userId, tenant, operationId, currentToken, markdownFile, "en", null, CancellationToken.None);
var completed = await WaitForTerminalAsync(service, tenant, job.JobId);
Assert.True(completed.ConversionSkipped);
Assert.Equal(0, externalHandler.RequestCount);
Assert.NotNull(completed.WizardResult);
```

- [x] **Step 2: Run import job tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeImportJobServiceTests" --verbosity minimal
```

Expected: FAIL because the unified import job does not exist.

- [x] **Step 3: Define BioTwin-owned job contracts**

```csharp
public sealed record ResumeImportStageDto(string Key, string Label, string Status, int Progress);

public sealed record ResumeImportJobDto(
    string JobId,
    string Status,
    int Progress,
    string CurrentStage,
    string Message,
    IReadOnlyList<ResumeImportStageDto> Stages,
    bool ConversionSkipped,
    ResumeWizardDto? WizardResult,
    IReadOnlyList<ConvertedResumeFileDto>? WorkspaceResults,
    string? SourceMarkdown,
    string? ErrorCode,
    string? Error,
    string? RecoveryHint,
    bool CanRetry);
```

Statuses are `queued`, `running`, `completed`, `failed`, and `canceled`. Stage messages are BioTwin-owned English UI strings and contain no provider identifiers.

- [x] **Step 4: Implement Markdown detection and conversion isolation**

Detect `.md`, `.markdown`, or `text/markdown`, decode with `new UTF8Encoding(false, true)`, and skip the external handler. For other formats, keep the provider job ID only inside API state and map provider progress into the API's Prepare range.

- [x] **Step 5: Implement full wizard and workspace processing**

Use `IServiceScopeFactory` so each background job resolves scoped `IResumeService`, `IResumeWizardExtractionService`, and `IResumeOperationService`. Wizard processing performs inspect, prepare, optional merge, and extraction. Workspace processing performs inspect and prepare for each file and returns all drafts. The singleton coordinator stores only copied bytes, status, results, and cancellation sources.

- [x] **Step 6: Implement cancellation and sanitized failures**

Cancel the job CTS, mark the job canceled, stop polling the provider, and release the operation lease only for explicit Cancel import. Map timeout, unsupported file, invalid UTF-8, provider failure, and extraction failure to stable error codes and recovery hints.

- [x] **Step 7: Run import job and conversion tests**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ResumeImportJobServiceTests|FullyQualifiedName~ResumeConversionJobServiceTests" --verbosity minimal
```

Expected: all selected tests pass.

---

### Task 5: Client API Contracts and Horizontal Progress Dialog

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Models/PendingResumeUpload.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/IResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Components/BlockingProgressOverlay.razor`
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Produces client methods for state, operation acquire/heartbeat/release, import start/status/cancel.
- Produces a progress dialog driven only by `ResumeImportJobDto`.

- [x] **Step 1: Write failing client architecture tests**

Assert the client contains `/api/resumes/import-jobs` and `/api/resumes/operations`, contains a horizontal stage rail and error action callbacks, and contains no `/convert/jobs`, `All2MD`, `MergePreviewAsync`, or `ExtractWizardAsync` usage in import pages.

- [x] **Step 2: Run Blazor architecture tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~Resume_imports" --verbosity minimal
```

Expected: FAIL because the client still orchestrates conversion, merge, and extraction.

- [x] **Step 3: Add pending upload and API methods**

```csharp
public sealed record PendingResumeUpload(string FileName, string ContentType, long Size, byte[] Content);

Task<ResumeOperationLeaseDto> AcquireOperationAsync(AcquireResumeOperationRequest request, CancellationToken cancellationToken = default);
Task HeartbeatOperationAsync(string operationId, CancellationToken cancellationToken = default);
Task ReleaseOperationAsync(string operationId, CancellationToken cancellationToken = default);
Task<ResumeImportJobDto> StartWizardImportAsync(PendingResumeUpload upload, string operationId, string expectedStateToken, string language, string? title, CancellationToken cancellationToken = default);
Task<ResumeImportJobDto> StartWorkspaceImportAsync(IReadOnlyList<PendingResumeUpload> uploads, string operationId, string expectedStateToken, CancellationToken cancellationToken = default);
Task<ResumeImportJobDto> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default);
Task CancelImportJobAsync(string jobId, CancellationToken cancellationToken = default);
```

- [x] **Step 4: Render the approved horizontal dialog**

Render stable equal-width stage tracks, overall percentage, current message, and statuses `completed`, `active`, `pending`, `skipped`, and `failed`. The error state shows reason, recovery hint, Retry when `CanRetry`, Choose another file for file errors, and Cancel import always. The running state always shows Cancel import.

- [x] **Step 5: Run Blazor architecture tests and verify GREEN**

Run the command from Step 2. Expected: selected tests pass.

---

### Task 6: Resume Creation Wizard Confirmation and Lease Lifecycle

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardStartStep.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeCreate.razor`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: `PendingResumeUpload`, operation APIs, and wizard import-job APIs from Task 5.
- Produces: no API call on file selection; first Continue acquires the lease; import result applies the API-provided `ResumeWizardDto`.

- [x] **Step 1: Write failing deferred-import tests**

Assert `FileSelected` stores pending bytes, `ContinueAsync` calls `AcquireOperationAsync` before `StartWizardImportAsync`, and the page does not call merge or extraction APIs.

- [x] **Step 2: Run the focused test and verify RED**

Run the Blazor command from Task 5 with the specific new test name. Expected: FAIL because selection currently calls `ImportAsync` immediately.

- [x] **Step 3: Implement local pending selection**

Read at most 10 MB into `PendingResumeUpload` during selection, show filename/type/size and Replace, and do not set `isBusy` or call `ResumeApiClient`.

- [x] **Step 4: Start the operation and API job from Continue**

Acquire using the latest `ResumeStateDto.Token`, start a heartbeat loop, then start and poll the wizard job. Apply `WizardResult` only at completion. Keep selected bytes for retry and saving the original file.

- [x] **Step 5: Implement cancel, retry, stale-state refresh, and cleanup**

Cancel the local CTS and API job, release the lease for Cancel import, retain the lease for Retry/Choose another, and refresh resume state before allowing retry after `resume_state_stale`. Save sends the operation ID and expected token, then releases the lease.

- [x] **Step 6: Run focused and full Blazor tests**

Expected: all tests pass and source checks confirm no client-side merge/extraction orchestration.

---

### Task 7: Workspace Confirmation and API-Managed Batch Import

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeWorkspace.razor`
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: workspace import job and operation APIs.
- Produces: pending removable file list and one explicit Import command.

- [x] **Step 1: Write failing workspace confirmation tests**

Assert `InputFile.OnChange` only builds pending uploads, each pending row has Remove, and an Import button starts the operation and batch job.

- [x] **Step 2: Run focused test and verify RED**

Expected: FAIL because `ImportAsync` currently starts conversion directly from `OnChange`.

- [x] **Step 3: Implement pending batch selection**

Keep confirmed files in order, allow removal, disable Import when empty, and cap each file at 10 MB. No API request occurs until Import.

- [x] **Step 4: Poll API-managed batch progress**

Render the API's current file, stages, and percentage. On completion, convert `WorkspaceResults` to local draft display models without additional processing calls.

- [x] **Step 5: Implement cancellation and errors**

Use the same operation lease, heartbeat, cancel, retry, choose-another, stale-state refresh, and release rules as the creation wizard.

- [x] **Step 6: Run focused and full Blazor tests**

Expected: all tests pass.

---

### Task 8: End-to-End Verification and Service Shutdown

**Files:**
- Verify all changed files.
- Do not commit.

- [x] **Step 1: Run all relevant test suites**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.DotNetShared.Tests\BioTwin_AI.DotNetShared.Tests.csproj -p:UseAppHost=false --verbosity minimal
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --verbosity minimal
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false --verbosity minimal
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.Tests\BioTwin_AI.Tests.csproj -p:UseAppHost=false --verbosity minimal
```

Expected: zero failed tests.

- [x] **Step 2: Build the solution**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build BioTwin_AI.slnx -p:UseAppHost=false --verbosity minimal
```

Expected: build succeeds with zero warnings and zero errors.

- [x] **Step 3: Verify the no-schema-change boundary**

Confirm no migration or seed script was added or changed for operation state, and source-check that `MemoryResumeOperationCoordinator` is registered as the only coordinator implementation while consumers depend only on `IResumeOperationCoordinator`. Expected: runtime lock state is entirely outside SQLite and the abstraction remains replaceable by a future Redis implementation.

- [ ] **Step 4: Verify behavior in a browser**

Blocked in this session: the in-app browser reported no available browser instances. API and client startup were checked, but interactive clicks and screenshots were not performed.

Start API and client, then verify selection does not create a network request, Continue/Import does, Markdown skips conversion, horizontal progress comes only from API state, a second client receives the active-operation error, cancel returns to pending selection, and stale state requires refresh. Capture desktop and mobile screenshots.

- [x] **Step 5: Check diff and shut down services**

Run `git diff --check` and inspect `git status --short`. Stop listeners on ports 8000, 5014, 5193, and any preview port started during verification. Do not commit.
