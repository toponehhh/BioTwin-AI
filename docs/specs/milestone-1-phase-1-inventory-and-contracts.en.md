# Milestone 1 Phase 1 Spec: Inventory and Architecture Contracts

> 中文版：`milestone-1-phase-1-inventory-and-contracts.zh-CN.md`

## 1. Goal

Phase 1 does not create new code projects and does not move existing code. Its output is a concrete specification built from the current `src/BioTwin_AI` pages, services, data models, and user workflows. This document becomes the boundary for Phase 2 project creation and Phase 3/4 implementation.

This phase confirms:

- Existing `src/BioTwin_AI` remains unchanged as the behavior reference and fallback baseline.
- Later phases will add `src/BioTwin_AI.BlazorClient`, `src/BioTwin_AI.AspNetCoreApi`, and `src/BioTwin_AI.DotNetShared`.
- All new test projects use `<ProjectName>.Tests` naming.
- The new API and WASM client are written from scratch. Old services and pages are behavior references only, not compatibility layers or adapter targets.
- The frontend uses Microsoft Fluent UI Blazor as the base component library, while the product direction remains a technical geek personal site plus AI workspace.

## 2. Current System Overview

The current application is a single ASP.NET Core web project:

- Host: `src/BioTwin_AI/Program.cs`
- UI: Blazor Server / Razor Components with `InteractiveServer`
- Data access: EF Core + SQLite, with the database under the runtime `database/biotwin.db`
- Logging: Serilog
- AI chat: `Microsoft.Extensions.AI` plus an OpenAI-compatible client
- Embedding: local BGE-M3 ONNX through `Microsoft.ML.OnnxRuntime.Extensions`
- RAG: current local service and database vector payloads
- PDF: QuestPDF
- Resume conversion: `ResumeUploadService` calls an All2MD HTTP service
- Localization: `.resx` plus `IStringLocalizer`

The current startup flow:

1. Creates the SQLite directory and DbContext.
2. Registers Razor Components and server interactive render mode.
3. Registers AI, embedding, RAG, auth, resume, and PDF services.
4. Runs `EnsureCreatedAsync()` for the database.
5. Runs `IRagService.InitializeAsync()` for RAG vector indexing.
6. Maps `/culture/set` and Razor Components.

## 3. Current Pages and User Workflows

The M1 frontend must cover the following pages and workflows. Old routes may remain useful compatibility entry points, but the new UI does not need to copy old layouts.

| Current route | Current file | Current dependencies | M1 coverage requirement |
| --- | --- | --- | --- |
| `/`, `/chat` | `Components/Pages/Index.razor` | `CurrentUserSession`, `AuthService`, `IJSRuntime`, `ChatInterfaceComponent` | The new home can become a personal technical homepage plus AI workspace entry. It must keep auth-state checks and the chat workflow entry. |
| `/login` | `Components/Pages/Login.razor` | `AuthService`, `CurrentUserSession`, `IJSRuntime` | Support login, registration, and interviewer mode. Reserve UI extension points for future external providers. |
| `/resume/upload` | `Components/Pages/ResumeUpload.razor` | `ResumeUploadService`, `AuthService`, `CurrentUserSession`, `IJSRuntime` | Support file selection, Markdown conversion, duplicate detection, section saving, and embedding rebuild. |
| `/resume/edit` | `Components/Pages/ResumeEdit.razor` | `BioTwinDbContext`, `IRagService`, `ResumeUploadService`, session/auth, JS | Move direct DbContext behavior behind APIs. M1 keeps only full Markdown editing and saving. The section tree is for structure preview and navigation only, with no single-section edit, duplicate, or delete actions. |
| `/resume/export` | `Components/Pages/ResumeExport.razor` | `BioTwinDbContext`, `ResumePdfExportService`, session/auth, JS | Support resume selection, Markdown preview, copy, Markdown download, PDF download, and original file download. |
| `/home` | `Components/Pages/Home.razor` | localizer | Can merge into the new personal home, but the functional entry should not disappear. |
| `/counter` | `Components/Pages/Counter.razor` | localizer | Sample page, not a business function. It does not need to stay in production navigation. |
| `/Error` | `Components/Pages/Error.razor` | diagnostics, localizer | After the split, use a frontend error page plus API `ProblemDetails`. |
| `/not-found` | `Components/Pages/NotFound.razor` | localizer | Keep a WASM 404 experience and cover Cloudflare SPA fallback. |

## 4. Current Component Inventory

| Component | Current responsibility | M1 component type | M1 handling |
| --- | --- | --- | --- |
| `ChatInterfaceComponent` | Questions, streaming answers, RAG citation display, message list | server API dependent | Replace service injection with typed `IChatApiClient`; streaming uses NDJSON. |
| `MarkdownEditor` | EasyMDE wrapper, value sync, line focus, JS interop | client UI + JS interop | Rebuild the wrapper in WASM and avoid server-only assumptions. |
| `ResumeSidebarComponent` | Loads resume sections and renders sidebar tree | server API dependent | Use `IResumeApiClient` to fetch the section tree. |
| `NavMenu` | Navigation, auth-state display, logout, collapse state | client state + API dependent | Move to Fluent UI navigation; logout calls API, theme switching also belongs in top/side navigation. |
| `MainLayout` | Application shell, culture switch, reconnect UI | client layout | Rebuild the WASM layout; language and theme switching must be reachable from the layout. |
| `ReconnectModal` | Blazor Server reconnect prompt | server-only | WASM no longer needs server circuit reconnect UI; replace with an API offline banner if needed. |

## 5. Current Service Capability Inventory

`BioTwin_AI.AspNetCoreApi` must re-implement the following capabilities from scratch in M1. Old class names are only behavior references.

| Current service | Current capability | New API module |
| --- | --- | --- |
| `AuthService` | Register, login, restore session, logout | `AuthController` / `SessionController` |
| `CurrentUserSession` | Server-scoped auth state, candidate/interviewer role, browser storage | Backend HttpOnly cookie session plus WASM `AuthenticationStateProvider` or a lightweight session store |
| `ResumeUploadService` | File to Markdown, save resume, save/replace sections, delete, rebuild embeddings, query entries/sections | `ResumesController`, `ResumeImportService`, `ResumeIndexingService` |
| `ResumeExportMarkdownBuilder` | Sections to Markdown, PDF title extraction | `ResumeExportService` |
| `ResumePdfExportService` | Markdown to PDF bytes | `ResumeExportController` |
| `ResumeMarkdownRefinementService` | AI-based Markdown refinement | `ResumeRefinementController` / `ResumeRefinementService` |
| `EmbeddingService` + `BgeM3OnnxEmbeddingModel` | Local BGE-M3 embedding | `EmbeddingService`, keeping the local ONNX implementation |
| `RagService` | Initialize index, create payloads, search, chat-context search, clear | `RagController`, `RagIndexService`, `RagSearchService` |
| `AgentService` | Non-streaming answers, streaming answers, structured stream chunks | `ChatController`, `ChatOrchestrationService` |
| `AiClientServiceCollectionExtensions` | Chat client and configuration registration | AI options plus service registration in the API project |

## 6. Current Data Model Inventory

Current EF entities must not be exposed directly to the WASM client. The M1 API should map them to DTOs.

| Entity | Current fields/relationship focus | M1 handling |
| --- | --- | --- |
| `UserAccount` | `Id`, `Username`, `PasswordHash`, `CreatedAt`; unique `Username` | Keep local accounts and add a persisted `Role` field plus an external identity extension table. |
| `ResumeEntry` | Title, original filename, original file content, content type, size, hash, tenant, sections | Return summary/detail DTOs. Do not return original file content except through a dedicated download endpoint. |
| `ResumeSection` | Resume parent, section parent/children, heading level, title, content, sort order, tenant, vector | Return tree-shaped section DTOs. M1 exposes no section mutation API; ordering and parent-child relationships are maintained from the full Markdown parse result. |
| `ResumeSectionVector` | Section id, tenant, title, parent title, content, embedding payload | Server-only. RAG/search APIs may return projected citation summaries. |
| `ResumeSectionChunk` | Section chunk metadata and payload | Server-only or projected shared search result metadata as needed. |

Recommended new M1 entity:

```text
UserExternalIdentity
  Id
  UserId
  Provider                  # GitHub, Google, Microsoft, CloudflareAccess, etc.
  ProviderUserId            # or Subject
  ProviderEmail
  ProviderEmailVerified
  ProviderDisplayName
  ProviderAvatarUrl
  LinkedAt
  LastLoginAt
  RawClaimsJson             # non-sensitive claims snapshot
```

Do not store OAuth access tokens or refresh tokens by default. If token storage becomes necessary later, design encryption, rotation, and least-privilege handling separately.

## 7. Target Project Responsibilities

```text
src/
  BioTwin_AI/               # Existing app; do not move, rename, or convert into the new API during M1
  BioTwin_AI.BlazorClient/  # New Blazor WASM frontend
  BioTwin_AI.AspNetCoreApi/ # New ASP.NET Core Web API
  BioTwin_AI.DotNetShared/  # DTOs, API contracts, shared constants
tests/
  BioTwin_AI.Tests/
  BioTwin_AI.BlazorClient.Tests/
  BioTwin_AI.AspNetCoreApi.Tests/
  BioTwin_AI.DotNetShared.Tests/
```

References:

- `BioTwin_AI.BlazorClient` -> `BioTwin_AI.DotNetShared`
- `BioTwin_AI.AspNetCoreApi` -> `BioTwin_AI.DotNetShared`
- `BioTwin_AI.BlazorClient.Tests` -> `BioTwin_AI.BlazorClient`, `BioTwin_AI.DotNetShared`
- `BioTwin_AI.AspNetCoreApi.Tests` -> `BioTwin_AI.AspNetCoreApi`, `BioTwin_AI.DotNetShared`
- `BioTwin_AI.DotNetShared.Tests` -> `BioTwin_AI.DotNetShared`

Forbidden references:

- `BioTwin_AI.BlazorClient` does not reference `BioTwin_AI.AspNetCoreApi`.
- `BioTwin_AI.BlazorClient` does not reference EF Core entities, DbContext, RAG services, AI services, or server secrets.
- `BioTwin_AI.DotNetShared` does not reference EF Core, ASP.NET Core hosting, QuestPDF, ONNX Runtime, sqlite, or AI clients.
- `BioTwin_AI.AspNetCoreApi` does not call old `src/BioTwin_AI` service implementations through adapters.

Code organization hard constraints:

- Business HTTP APIs in `BioTwin_AI.AspNetCoreApi` must use MVC/Web API controllers. Do not use Minimal APIs, route groups, or centralized `MapGet`/`MapPost` business endpoints in `Program.cs`.
- Each feature area must have its own controller, for example `AuthController`, `SessionController`, `ResumesController`, `ResumeExportController`, `ResumeRefinementController`, `ChatController`, `RagController`, and `HealthController`.
- Every class, record, enum, and interface must live in its own `.cs` file. Do not create catch-all files such as `Dtos.cs`, `Requests.cs`, `Responses.cs`, or `Models.cs` that bundle multiple types together.
- Types may be grouped by feature folders, for example `Contracts/Auth/LoginRequest.cs` and `Contracts/Auth/AuthResult.cs`, but multiple distinct types must not share a single file.

## 8. Draft API Route Contracts

APIs return JSON by default. Errors return `ProblemDetails`. The M1 session mechanism is backend-issued HttpOnly cookies; production must pair this with HTTPS, `Secure=true`, a deliberate `SameSite` policy, and restricted CORS. Every route is exposed by its corresponding controller and is not implemented with Minimal APIs.

### Auth and Session

| Method | Route | Request | Response | Notes |
| --- | --- | --- | --- | --- |
| `GET` | `/api/session/current` | - | `CurrentSessionResponse` | Returns auth state, user, role, and available providers. |
| `POST` | `/api/auth/register` | `RegisterRequest` | `AuthResult` | Local account registration. |
| `POST` | `/api/auth/login` | `LoginRequest` | `AuthResult` | Local account login. |
| `POST` | `/api/auth/interviewer-login` | - | `AuthResult` | Dev/demo shortcut only; production authorization comes from persisted user roles. |
| `POST` | `/api/auth/logout` | - | `NoContent` | Clears server session/cookie. |
| `GET` | `/api/auth/external/providers` | - | `ExternalIdentityProviderDto[]` | Returns future provider display state; M1 may return disabled providers. |

### Resumes

| Method | Route | Request | Response | Notes |
| --- | --- | --- | --- | --- |
| `GET` | `/api/resumes` | query | `ResumeSummaryDto[]` | Resume list. |
| `GET` | `/api/resumes/{resumeId}` | - | `ResumeDetailDto` | Resume details and section tree. |
| `POST` | `/api/resumes/upload/convert` | `multipart/form-data` | `ConvertedResumeFileDto` | Upload and convert to Markdown without saving. |
| `POST` | `/api/resumes` | `SaveResumeMarkdownRequest` | `ResumeDetailDto` | Save a new resume and index it. |
| `PUT` | `/api/resumes/{resumeId}/markdown` | `SaveResumeMarkdownRequest` | `ResumeDetailDto` | Replace sections from full Markdown and rebuild index. |
| `DELETE` | `/api/resumes/{resumeId}` | - | `NoContent` | Delete resume, sections, and vectors. |
| `POST` | `/api/resumes/rebuild-embeddings` | - | `RebuildEmbeddingsResponse` | Rebuild all vectors. |
| `GET` | `/api/resumes/{resumeId}/original` | - | file | Download original uploaded file. |

### Resume Sections

M1 does not expose standalone section mutation APIs. The section tree is returned by `GET /api/resumes/{resumeId}` and is used by the frontend for structure preview and navigation only. All section changes happen through `PUT /api/resumes/{resumeId}/markdown`, where the server receives full Markdown, reparses sections, saves them, and rebuilds the index.

### Export and Refinement

| Method | Route | Request | Response | Notes |
| --- | --- | --- | --- | --- |
| `GET` | `/api/resumes/{resumeId}/export/markdown` | - | `ResumeMarkdownExportDto` | Returns full Markdown. |
| `GET` | `/api/resumes/{resumeId}/export/pdf` | - | file | Returns PDF bytes. |
| `POST` | `/api/resumes/refine` | `RefineMarkdownRequest` | `RefineMarkdownResponse` | Refines Markdown. |

### Chat and RAG

| Method | Route | Request | Response | Notes |
| --- | --- | --- | --- | --- |
| `POST` | `/api/chat` | `ChatRequest` | `ChatResponse` | Non-streaming answer. |
| `POST` | `/api/chat/stream` | `ChatRequest` | NDJSON `ChatStreamChunk` | Streaming answer; each line is one JSON chunk. |
| `POST` | `/api/rag/search` | `RagSearchRequest` | `RagSearchResponse` | Searches resume context. |
| `GET` | `/api/health` | - | `HealthResponse` | Health check. |

## 9. `BioTwin_AI.DotNetShared` DTO Draft

Recommended namespaces:

- `BioTwin_AI.DotNetShared.Auth`
- `BioTwin_AI.DotNetShared.Resumes`
- `BioTwin_AI.DotNetShared.Chat`
- `BioTwin_AI.DotNetShared.Rag`
- `BioTwin_AI.DotNetShared.Common`

File organization rules:

- Every DTO/record listed below should be created as its own file.
- File paths should be grouped by namespace or feature area, for example `Auth/LoginRequest.cs`, `Resumes/ResumeSummaryDto.cs`, and `Chat/ChatRequest.cs`.
- `BioTwin_AI.DotNetShared` must not use one large file for all DTOs, and must not place request, response, summary, and detail types together in the same file.

Core DTOs:

```csharp
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record CurrentSessionResponse(
    bool IsAuthenticated,
    string? Username,
    string? DisplayName,
    string Role,
    IReadOnlyList<ExternalIdentityProviderDto> ExternalProviders);

public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record AuthResult(bool Success, string Message, CurrentSessionResponse? Session);
public sealed record ExternalIdentityProviderDto(string Provider, string DisplayName, bool IsEnabled, bool IsLinked);

public sealed record ResumeSummaryDto(
    int Id,
    string Title,
    string? SourceFileName,
    DateTimeOffset CreatedAt,
    int SectionCount,
    bool HasOriginalFile);

public sealed record ResumeDetailDto(
    int Id,
    string Title,
    string? SourceFileName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ResumeSectionDto> Sections);

public sealed record ResumeSectionDto(
    int Id,
    int ResumeEntryId,
    int? ParentSectionId,
    int HeadingLevel,
    string Title,
    string Content,
    int SortOrder,
    IReadOnlyList<ResumeSectionDto> Children);

public sealed record ConvertedResumeFileDto(
    string Title,
    string SourceFileName,
    string Markdown,
    bool IsDuplicate,
    int? ExistingResumeEntryId,
    string? ExistingResumeTitle);

public sealed record SaveResumeMarkdownRequest(
    string Title,
    string Markdown,
    string? SourceFileName,
    string? SourceContentType,
    long? SourceFileSize);

public sealed record ResumeMarkdownExportDto(string FileName, string Markdown);
public sealed record RebuildEmbeddingsResponse(int ResumeCount, int SectionCount);

public sealed record RefineMarkdownRequest(string Markdown, string? ResumeTitle);
public sealed record RefineMarkdownResponse(string Markdown);

public sealed record ChatRequest(string Question, IReadOnlyList<ChatMessageDto>? History = null);
public sealed record ChatMessageDto(string Role, string Content);
public sealed record ChatResponse(string Answer, IReadOnlyList<RagCitationDto> Citations);
public sealed record ChatStreamChunk(string Kind, string Content);

public sealed record RagSearchRequest(string Query, int Limit = 5);
public sealed record RagSearchResponse(IReadOnlyList<RagCitationDto> Results);
public sealed record RagCitationDto(
    int ResumeEntryId,
    int ResumeSectionId,
    string ResumeTitle,
    string SectionTitle,
    string ContentPreview,
    float Score);
```

## 10. New Frontend Information Architecture

`BioTwin_AI.BlazorClient` must cover old business capabilities in the first phase while redesigning the experience as a technical geek personal site.

Recommended pages:

| New route | Purpose |
| --- | --- |
| `/` | Technical personal home: identity intro, skill matrix, project entry, AI/RAG entry, recent resume state. |
| `/skills` | Skills, tech stacks, AI/Cloud/.NET/Cloudflare migration capability display. |
| `/projects` | Project/experience timeline, optionally linked to resume sections. |
| `/chat` | AI chat plus RAG citation workspace. |
| `/resume` | Resume overview and latest versions. |
| `/resume/upload` | Upload, convert, save, and rebuild indexes. |
| `/resume/edit/{resumeId?}` | Full Markdown editing and saving. The section tree is for structure preview, focus, and navigation only, with no single-section editing. |
| `/resume/export/{resumeId?}` | Markdown/PDF/original file export. |
| `/login` | Login, registration, interviewer entry, future external-login placeholders. |
| `/settings` | Theme, language, API endpoint, and future provider-link management. |

UI constraints:

- Use Microsoft Fluent UI Blazor components.
- Support at least light/dark themes and persist the choice to browser storage.
- Theme tokens cover background, text, border, buttons, code areas, terminal-inspired areas, and status colors.
- Do not use the default enterprise dashboard look as the whole visual identity. The design should feel like portfolio plus AI workspace.
- Existing pages are only the functional coverage checklist and do not constrain layout.

## 11. New Backend Module Boundaries

Recommended `BioTwin_AI.AspNetCoreApi` layering:

```text
Controllers/
  AuthController
  SessionController
  ResumesController
  ResumeExportController
  ResumeRefinementController
  ChatController
  RagController
  HealthController

Application/
  Auth/
  Resumes/
  Chat/
  Rag/
  Export/

Infrastructure/
  Data/
  Ai/
  Embeddings/
  Pdf/
  Files/
  Logging/
```

Rules:

- Backend business endpoints must all live in controllers. Minimal APIs are not allowed for business endpoint implementation.
- Each controller owns one feature area. Do not create a giant `ApiController` or mix unrelated business domains in one controller.
- Controllers handle HTTP, validation, DTO mapping, and status codes only.
- Application services own business workflows.
- Infrastructure owns EF Core, AI clients, ONNX, QuestPDF, and HTTP integrations.
- Every API returns DTOs, not EF entities.
- All server secrets stay in API project configuration.
- Every controller, service, option type, entity, DTO, request, response, enum, and interface should use its own `.cs` file with a filename matching the type name.

## 12. Test Mapping

Existing `tests/BioTwin_AI.Tests` remains the behavior reference. M1 tests should move into the matching project.

| Existing test area | New test project |
| --- | --- |
| Auth/session tests | `BioTwin_AI.AspNetCoreApi.Tests` plus necessary `BioTwin_AI.BlazorClient.Tests` session store tests |
| Agent/chat tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| RAG tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| Embedding tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| Resume upload/delete/refine/export tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| DTO validation/serialization tests | `BioTwin_AI.DotNetShared.Tests` |
| Frontend route/API client/theme tests | `BioTwin_AI.BlazorClient.Tests` |
| Serilog/config/dependency tests | `BioTwin_AI.AspNetCoreApi.Tests` |

Later implementation should include at least:

- API integration tests for auth, resume upload/save/edit/delete/export, chat, and RAG search.
- DTO serialization tests to keep the WASM/API contract stable.
- Frontend tests for theme switching, route authorization, and API client error handling.

## 13. Phase 1 Deliverables and Acceptance

This spec completes Phase 1 when:

- Current pages, components, services, data models, and user workflows are listed.
- New project responsibilities, references, and forbidden dependencies are defined.
- API route map and DTO draft are defined.
- New frontend information architecture and Fluent UI Blazor constraints are defined.
- Local account and external identity provider extension points are defined.
- Future test split direction is defined.

Phase 1 does not include:

- Creating new `.csproj` files.
- Moving or renaming existing code.
- Implementing API controllers.
- Implementing the Blazor WASM UI.
- Changing database migrations.

## 14. Confirmed Decisions Before Phase 2

The confirmation points before Phase 2 are now decided:

1. API sessions use backend-issued HttpOnly cookies. Short-lived bearer tokens are not the default M1 login-state mechanism.
2. `/api/chat/stream` uses NDJSON streaming while keeping `POST` and the `ChatRequest` request body.
3. The new frontend home is the `/` personal homepage. `/chat` is not the homepage. The homepage content, module order, and copy must be confirmed separately during frontend design.
4. `interviewer` is no longer only temporary UI state. M1 persists user roles in the local account model, with at least `Candidate`, `Interviewer`, and `Admin`. API authorization uses persisted roles.
5. `ResumeEdit` only supports full Markdown editing and saving. It does not provide single-section edit, duplicate, or delete actions. The section tree is only for structure preview, focus, and navigation.
