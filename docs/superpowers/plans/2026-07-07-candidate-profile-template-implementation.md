# Candidate Profile Template Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build public candidate profile sharing with short rotating profile codes, multi-role users, versioned semi-structured profile information, and reusable timeline template rendering.

**Architecture:** The ASP.NET Core API owns identity, profile-code generation, profile info versioning, public profile resolution, and resume-save invalidation. Shared DTOs live in `BioTwin_AI.DotNetShared`; the Blazor client consumes the public profile endpoint and renders one profile template for default and `uid`-selected candidates. Existing resume storage remains tenant-based for now, while profile data is user-bound by authenticated user ID.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core SQLite, xUnit, Blazor WebAssembly, `System.Text.Json`, `RandomNumberGenerator`.

## Global Constraints

- Keep English and Chinese specs aligned in separate files.
- `ProfileHash` is a short public share code, default 8 characters, with 6 as configurable lower bound.
- Share codes use an unambiguous uppercase alphabet and are normalized before lookup.
- Anonymous public lookup must return generic unavailable responses and be rate-limit ready.
- Roles are many-to-many through `UserRoles`; cookie auth emits one role claim per assigned role.
- `CandidateProfileInfos` stores versioned JSON by `InfoType`; LLM generation and manual edits create new versions.
- Resume/profile visible content changes rotate `ProfileHash`.
- Do not overwrite unrelated dirty Blazor UI changes already present in the worktree.
- Use `C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe` for build/test verification.

---

### Task 1: Shared Contracts

**Files:**
- Modify: `src/BioTwin_AI.DotNetShared/Auth/CurrentSessionResponse.cs`
- Create: `src/BioTwin_AI.DotNetShared/Profiles/CandidateProfileDto.cs`
- Create: `src/BioTwin_AI.DotNetShared/Profiles/CandidateProfileInfoDto.cs`
- Test: `tests/BioTwin_AI.DotNetShared.Tests/Contracts/PhaseOneContractTests.cs`

**Interfaces:**
- Produces `CurrentSessionResponse.Roles`.
- Produces `CandidateProfileDto`, `CareerTimelineItemDto`, `WorkTimelineItemDto`, and profile info DTOs.

- [ ] Add failing contract tests for multi-role session and public profile DTO shape.
- [ ] Update shared records to satisfy tests.
- [ ] Run `dotnet test tests/BioTwin_AI.DotNetShared.Tests/BioTwin_AI.DotNetShared.Tests.csproj -p:UseAppHost=false`.

### Task 2: Identity and Database Shape

**Files:**
- Modify: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Entities/UserAccount.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Entities/UserRoleAssignment.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Entities/CandidateProfileInfo.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/BioTwinApiDbContext.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Migrations/004-add-candidate-profile-sharing.sql`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Architecture/PhaseImplementationTests.cs`

**Interfaces:**
- Produces EF sets `UserRoles` and `CandidateProfileInfos`.
- Produces columns `ProfileHash`, `ProfileHashUpdatedAt`, `CandidateProfileVersion`, `IsProfilePublic`, `IsDefaultCandidate`.

- [ ] Add failing architecture tests for new entities, mappings, and migration script.
- [ ] Implement entities and DbContext mappings.
- [ ] Add startup schema backfill for existing SQLite databases.
- [ ] Run focused API architecture tests.

### Task 3: Profile Code and Role Services

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/IProfileShareCodeGenerator.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/ProfileShareCodeGenerator.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Auth/IUserRoleService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Auth/UserRoleService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Auth/AuthService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Auth/SessionResponseFactory.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/AuthController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/SessionController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ProfileShareCodeGeneratorTests.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/UserRoleServiceTests.cs`

**Interfaces:**
- Produces `ProfileShareCodeGenerator.Generate()`.
- Produces role assignment helpers and multiple role claims.

- [ ] Add failing tests for 8-character normalized share code generation and collision-friendly uniqueness.
- [ ] Add failing tests for candidate/interviewer role assignment.
- [ ] Implement services and update auth/session responses.
- [ ] Run focused tests.

### Task 4: Versioned Profile Information

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/ICandidateProfileInfoService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/CandidateProfileInfoService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/CandidateProfileExtractionService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/CandidateProfileInfoServiceTests.cs`

**Interfaces:**
- Produces `CreateNextVersionAsync`, `GetCurrentAsync`, and `GenerateFromResumeAsync`.
- Generates first-pass `careertimeline` and `worktimeline` JSON with deterministic fallback parsing when LLM is unavailable or returns invalid JSON.

- [ ] Add failing tests for creating new current versions and preserving older versions.
- [ ] Add failing tests for regeneration using previous current JSON as reference context.
- [ ] Implement service and simple schema validation.
- [ ] Run focused tests.

### Task 5: Public Profile API

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/IPublicCandidateProfileService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Profiles/PublicCandidateProfileService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Controllers/PublicCandidateProfileController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/PublicCandidateProfileServiceTests.cs`

**Interfaces:**
- Produces `GetAsync(string? uid, CancellationToken)`.
- Public controller route is `GET /api/public/candidate-profile?uid=<code>`.

- [ ] Add failing tests for default candidate resolution.
- [ ] Add failing tests for uid lookup, case normalization, private/deleted rejection, and generic not found.
- [ ] Implement service and controller.
- [ ] Run focused tests.

### Task 6: Resume Save Integration

**Files:**
- Modify: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeService.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Test: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeProfileIntegrationTests.cs`

**Interfaces:**
- Resume saves receive authenticated user ID from controller.
- Resume saves rotate `ProfileHash`, increment `CandidateProfileVersion`, and refresh current `CandidateProfileInfos`.

- [ ] Add failing tests that saving resume rotates profile code and creates profile info versions.
- [ ] Update resume controller/service signatures.
- [ ] Implement rotation and generation in the same save flow.
- [ ] Run focused tests.

### Task 7: Blazor Public Profile Template

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Services/Api/IPublicProfileApiClient.cs`
- Create: `src/BioTwin_AI.BlazorClient/Services/Api/PublicProfileApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Program.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Home.razor`
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Produces Blazor API client `GetCandidateProfileAsync(string? uid, CancellationToken)`.
- Home page reads `uid` query string and renders candidate template with vertical and horizontal timelines.

- [ ] Add failing architecture tests for public profile client and template markers.
- [ ] Implement API client registration.
- [ ] Render public profile template on home page while preserving existing floating menu layout.
- [ ] Run focused Blazor tests.

### Task 8: Verification and Commit

**Files:**
- All touched source and test files.

**Interfaces:**
- Produces buildable/tested implementation.

- [ ] Run API tests.
- [ ] Run shared contract tests.
- [ ] Run Blazor client tests.
- [ ] Run solution build with explicit SDK path.
- [ ] Commit implementation changes without unrelated dirty UI files unless they were intentionally part of Task 7.
