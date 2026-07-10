# All2MD Progress Jobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace long synchronous resume conversion requests with authenticated asynchronous jobs that expose All2MD's real progress and block conflicting UI actions while conversion runs.

**Architecture:** BioTwin starts an All2MD `/convert/jobs` job, stores a tenant-scoped mapping in memory, and proxies `/convert/jobs/{job_id}` progress through authenticated BioTwin endpoints. Blazor starts the BioTwin job and polls every two seconds, showing the returned percentage and message in a full-screen modal overlay until a `ConvertedResumeFileDto` result is available.

**Tech Stack:** .NET 10, ASP.NET Core controllers and `HttpClientFactory`, Blazor WebAssembly, xUnit, All2MD FastAPI job API.

## Global Constraints

- Use the real All2MD `progress` and `message` fields; do not simulate percentage values.
- Scope every BioTwin conversion job to the authenticated tenant.
- Enforce `All2MD:TimeoutSeconds` as the maximum job lifetime.
- Keep `POST /api/resumes/upload/convert` for compatibility, but new UI flows must use job endpoints.
- Apply the blocking progress UI to both Resume Create and Resume Workspace.
- Do not commit changes unless the user explicitly asks.
- After verification, shut down every related service used during verification.

---

### Task 1: Shared Conversion Job Contracts

**Files:**
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeConversionJobDto.cs`
- Modify: `tests/BioTwin_AI.DotNetShared.Tests/Contracts/PhaseOneContractTests.cs`

**Interfaces:**
- Produces: `ResumeConversionJobDto(string JobId, string Status, int Progress, string Message, ConvertedResumeFileDto? Result, string? Error)`.

- [ ] Add a failing contract test that constructs queued and completed job responses and verifies progress and result fields.
- [ ] Run the focused shared test and confirm compilation fails because `ResumeConversionJobDto` is missing.
- [ ] Add the immutable record with statuses `queued`, `running`, `completed`, and `failed` represented as response strings.
- [ ] Run the shared test project and confirm all tests pass.

### Task 2: Authenticated BioTwin Job Proxy

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeConversionJobService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeConversionJobService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeConversionJobServiceTests.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`

**Interfaces:**
- Produces: `StartAsync(string tenantId, IFormFile file, CancellationToken)` and `GetAsync(string tenantId, string jobId, CancellationToken)`.
- Produces: `POST /api/resumes/upload/jobs` and `GET /api/resumes/upload/jobs/{jobId}`.

- [ ] Write failing tests using a fake All2MD handler for queued, running, completed, failed, wrong-tenant, and 600-second expiration behavior.
- [ ] Implement a singleton service with a `ConcurrentDictionary` mapping BioTwin job IDs to tenant, All2MD job ID, filename, content type, and start time.
- [ ] On start, post multipart content to `{All2MD:ApiUrl}/convert/jobs` and return the real queued progress.
- [ ] On status, call `{All2MD:ApiUrl}/convert/jobs/{all2MdJobId}`, validate tenant ownership, map progress, and construct `ConvertedResumeFileDto` when completed.
- [ ] Register the service and add authorized controller endpoints.
- [ ] Run the API test project and confirm all tests pass.

### Task 3: Blazor Polling Client and Blocking Overlay

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Components/BlockingProgressOverlay.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/IResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeCreate.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeWorkspace.razor`
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Modify: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Produces: `StartConversionJobAsync(...)` and `GetConversionJobAsync(jobId, ...)`.
- Consumes: real `Progress`, `Message`, and `Result` from `ResumeConversionJobDto`.

- [ ] Add failing source-contract tests for both pages using job APIs, a full-screen modal overlay, a native progress element, and no synchronous `ConvertUploadAsync` call.
- [ ] Implement the accessible overlay with `role="dialog"`, `aria-modal="true"`, progress percentage, message, and interaction-blocking fixed positioning above the Header.
- [ ] Implement a two-second polling helper that exits on completed/failed states and preserves UI state on errors.
- [ ] Replace conversion calls in Resume Create and Resume Workspace with the job flow.
- [ ] Disable page commands while conversion runs and keep the overlay visible for the whole operation.
- [ ] Run the Blazor tests and client build.

### Task 4: End-to-End Verification and Shutdown

**Files:**
- Verify all files from Tasks 1-3.

**Interfaces:**
- Confirms All2MD progress is propagated without a long browser request.

- [ ] Run shared, API, and Blazor test projects with the configured .NET SDK.
- [ ] Build `BioTwin_AI.slnx` and the Blazor client with zero errors.
- [ ] Start All2MD, BioTwin API, and Blazor only if live verification is needed; verify short job-start and polling requests.
- [ ] Confirm no synchronous UI conversion call remains.
- [ ] Shut down All2MD, BioTwin API, and Blazor services used for verification.
- [ ] Run `git diff --check` and report modified files without committing.
