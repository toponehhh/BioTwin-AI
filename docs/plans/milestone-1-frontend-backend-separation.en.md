# Milestone 1 Plan: Frontend/Backend Separation for Cloudflare Migration

> 中文版：`milestone-1-frontend-backend-separation.zh-CN.md`

### 1. Context and Goal

BioTwin_AI is currently a single ASP.NET Core web application that mixes backend services, data access, RAG, AI calls, and Blazor/Razor UI hosting. The long-term goal is to move the whole system onto Cloudflare resources wherever practical: frontend hosting, API execution, SQL data, object storage, vector search, and AI inference.

Milestone 1 does not attempt the full Cloudflare migration. It focuses on the most important architectural split:

- Move the frontend into a standalone Blazor WebAssembly project that can later be deployed to Cloudflare Pages or Workers Static Assets.
- Keep the backend on ASP.NET Core Web API for now.
- Match the current application's functional scope, but write the new project code from scratch. Existing code is a behavior reference only, not a compatibility layer or copy target.
- Prepare clean boundaries for later migration to Cloudflare D1, R2, Vectorize, Workers AI, Containers, or Workers APIs.
- Keep all existing code and folders unchanged during this milestone. The split should be done by adding new projects first, preserving the current application as a runnable baseline.

### 2. Cloudflare Constraints and Migration Direction

Based on Cloudflare documentation:

- Cloudflare Pages supports Blazor WebAssembly deployment. Blazor Server is not compatible with Cloudflare's edge network model, so the frontend should move to Blazor WASM.
- Workers Static Assets supports SPA fallback to `index.html`, which fits Blazor WASM routing.
- Cloudflare D1 provides managed serverless SQLite semantics accessible from Workers and Pages projects.
- Cloudflare R2 is suitable for uploaded resumes, generated PDFs, model artifacts, and large objects.
- Cloudflare Vectorize can later replace local serialized vector payloads or sqlite-vector style retrieval.
- Cloudflare Workers AI can be invoked from Workers, Pages, or APIs and may later replace local/external AI calls.
- Cloudflare Containers can route Worker requests to container instances, which may be a transition path for ASP.NET Core API hosting, but this is outside Milestone 1.

The Milestone 1 strategy is to split boundaries first. Do not force ASP.NET Core into Workers yet. Keep the backend runnable and stable, then decide later whether to deploy it through Cloudflare Containers or rewrite modules as Workers APIs.

### 3. Milestone 1 Scope

#### In Scope

- Create a standalone frontend project named `src/BioTwin_AI.BlazorClient`.
- Implement all current application pages and user workflows in `BioTwin_AI.BlazorClient`. Existing pages are a functional checklist only.
- Redesign the frontend UI. The target style is a technical geek personal site that can fully present current skills, projects, resume, and AI/RAG capabilities.
- Add theme switching with at least light and dark themes, leaving room for more theme tokens later.
- Create a new backend API project named `src/BioTwin_AI.AspNetCoreApi`, keeping ASP.NET Core Web API as the backend technology for now.
- `BioTwin_AI.AspNetCoreApi` must implement all current service capabilities in the first phase. Do not preserve compatibility with old services, and do not reuse old code through adapters. Feature behavior may reference the old implementation.
- Create a shared contract project named `src/BioTwin_AI.DotNetShared` for DTOs, API contracts, and lightweight shared constants across .NET projects.
- Keep the existing `src/BioTwin_AI`, existing resource folders, and existing test folders in place without moving or renaming them. They remain the migration reference and fallback baseline.
- Replace UI service injection with typed HTTP clients.
- Gradually remove server-rendered UI hosting and Blazor Server dependencies from the API boundary.
- Define API DTOs and routes so the client does not directly reference EF entities or internal service models.
- Configure CORS, API base URL, frontend static publish output, and local two-process development scripts.
- Add frontend/backend build and test verification. Every test project must use the corresponding project name plus `.Tests`.

#### Out of Scope

- Migrating the database to Cloudflare D1.
- Migrating files to R2.
- Migrating vector retrieval to Vectorize.
- Replacing local BGE-M3 ONNX embeddings with Workers AI.
- Completing production deployment on Cloudflare Containers.
- Introducing paid UI component libraries or paid design tooling.

### 4. Recommended Target Structure

```text
BioTwin_AI.slnx
src/
  BioTwin_AI/               # Existing app; keep unchanged as baseline during milestone 1
  BioTwin_AI.BlazorClient/  # New Blazor WebAssembly frontend
  BioTwin_AI.AspNetCoreApi/ # New ASP.NET Core Web API backend
  BioTwin_AI.DotNetShared/  # DTOs, API contracts, shared validation constants
tests/
  BioTwin_AI.Tests/                 # Existing tests; keep unchanged initially
  BioTwin_AI.BlazorClient.Tests/    # Tests for BioTwin_AI.BlazorClient
  BioTwin_AI.AspNetCoreApi.Tests/   # Tests for BioTwin_AI.AspNetCoreApi
  BioTwin_AI.DotNetShared.Tests/    # Tests for BioTwin_AI.DotNetShared
docs/
  plans/
```

Rules:

- `BioTwin_AI` is the existing application. During Milestone 1 it is not moved, renamed, or converted in place into the new API project.
- `BioTwin_AI.DotNetShared` contains stable API contracts only. It must not contain EF Core, RAG, AI clients, database access, or server-only code.
- `BioTwin_AI.BlazorClient` communicates with the backend only through HTTP APIs and does not reference backend services directly.
- `BioTwin_AI.AspNetCoreApi` owns the new Web API boundary and re-implements server-side services for the current application capabilities. The old `BioTwin_AI` project is a behavior reference only.
- Project names intentionally include their current technology stack so future rewrites can coexist clearly, for example a future `BioTwin_AI.WorkersApi` or another non-.NET implementation.
- Test project names must always follow `<ProjectName>.Tests`.

### 5. Draft API Boundary

Initial API routes should follow the current UI workflows:

```text
GET    /api/session/current
POST   /api/auth/login
POST   /api/auth/logout

GET    /api/resumes
GET    /api/resumes/{resumeId}
POST   /api/resumes/upload
PUT    /api/resumes/{resumeId}/markdown
POST   /api/resumes/{resumeId}/refine
GET    /api/resumes/{resumeId}/export/pdf

POST   /api/chat
POST   /api/rag/search
GET    /api/health
```

DTO rules:

- Request and response models live in `BioTwin_AI.DotNetShared`.
- DTOs are workflow-oriented, for example `ResumeSummaryDto`, `ResumeDetailDto`, `ChatRequestDto`, and `ChatResponseDto`.
- APIs do not return EF entities.
- File upload remains multipart/form-data for now. A future R2 migration can introduce direct uploads or presigned upload URLs.

### 6. Frontend Rebuild and Design Strategy

Milestone 1 is not a straight UI port. The new `BioTwin_AI.BlazorClient` should rebuild the full experience:

1. Create an empty Blazor WASM project and prove routing, layout, localization, CSS, theme tokens, and API base URL configuration.
2. Establish the new information architecture: personal technical home, skills showcase, project/experience showcase, resume management, Markdown editing, PDF export, AI chat/RAG, and every current page capability must have a WASM version.
3. Use a technical geek personal-site direction: more portfolio + AI workspace than traditional admin dashboard. The UI can use terminal-inspired surfaces, code-inspired details, technology tags, project timelines, skill matrices, and light/dark theme switching.
4. Add theme switching. Theme state must persist in browser storage across reloads. Theme tokens should cover base colors, backgrounds, text, borders, code/terminal areas, and interaction states.
5. The third-party UI component library is explicitly Microsoft Fluent UI Blazor: https://www.fluentui-blazor.net/.
6. Fluent UI Blazor is the base component layer only. The overall visual direction still needs a custom technical geek personal-site + AI workspace design; do not simply apply a default enterprise dashboard style.
7. Existing pages are only a functional coverage checklist. They should not constrain the new UI layout, component structure, or visual style.
8. Keep the existing `BioTwin_AI` server-rendered UI temporarily runnable until the WASM client covers core workflows, then discuss whether to retire it.

For components that currently depend heavily on server services, introduce client-side interfaces first:

```csharp
public interface IResumeApiClient
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default);
}
```

Then implement that interface in the WASM project with an HTTP client.

### 7. Backend Rebuild Strategy

The new `BioTwin_AI.AspNetCoreApi` backend remains ASP.NET Core during Milestone 1:

- The new API project's `Program.cs` targets Web API hosting. Do not convert the existing `BioTwin_AI/Program.cs` in place.
- Implement all current service capabilities in the first phase, including auth, session, resume upload/parsing, Markdown refinement, PDF export, RAG, embedding, AI chat, database access, and required health checks.
- Backend code is written from scratch. Do not add a compatibility layer for old services, and do not call old project code through adapters. The old implementation is only a behavior reference.
- Keep the existing technology choices for Milestone 1: Serilog, EF Core, SQLite, RAG, current embedding, AI chat, resume refinement, and QuestPDF. This reduces platform migration variables.
- Add controllers or minimal APIs. Controllers are recommended for clearer grouping and tests.
- Add CORS. In development, allow the WASM dev origin. In production, allow only the Cloudflare Pages domain.
- Convert auth/session from server-side scoped UI state to an API-friendly cookie or token model.

Authentication needs careful discussion: Blazor WASM cannot safely hold backend secrets. Login state should be represented by backend-issued HttpOnly cookies or short-lived tokens. The client must never contain OpenRouter, database, or server credentials.

The local account schema must reserve future external identity support in Milestone 1. Design a `UserExternalIdentity` table or equivalent structure with at least:

- `UserId`
- `Provider`, such as `GitHub`, `Google`, `Microsoft`, `CloudflareAccess`
- `ProviderUserId` or `Subject`
- `ProviderEmail`
- `ProviderEmailVerified`
- `ProviderDisplayName`
- `ProviderAvatarUrl`
- `LinkedAt`
- `LastLoginAt`
- `RawClaimsJson`, for a non-sensitive claims snapshot

Do not store OAuth access tokens or refresh tokens by default. If future milestones require token storage, design encryption, rotation, and least-privilege handling separately.

### 8. Cloudflare Alignment

After Milestone 1:

- Frontend: Blazor WASM `wwwroot` publish output can be deployed to Cloudflare Pages or Workers Static Assets.
- Backend short term: ASP.NET Core API can continue as Docker/local hosting. If Cloudflare Containers is selected, a Worker can route requests to the container.
- Backend long term: API modules can be migrated to Workers one at a time.
- Database: SQLite/EF schema becomes input for D1 migration.
- Files: uploads, PDFs, and model files can move to R2.
- Vectors: current RAG vectors can move to Vectorize.
- AI: current chat and embedding abstractions can move to Workers AI or AI Gateway.

### 9. Suggested Implementation Phases

#### Phase 1: Inventory and contracts

- List current pages, components, services, and user workflows.
- Define the current page list as the required Milestone 1 coverage scope, not as optional migration candidates.
- Mark each component as pure UI, client state, server API dependent, or server-only.
- Draft `BioTwin_AI.DotNetShared` DTOs.
- Define the API route map.

#### Phase 2: Solution split

- Create `BioTwin_AI.BlazorClient` Blazor WASM project.
- Create `BioTwin_AI.AspNetCoreApi` ASP.NET Core Web API project.
- Create `BioTwin_AI.DotNetShared` class library.
- Create matching test projects: `BioTwin_AI.BlazorClient.Tests`, `BioTwin_AI.AspNetCoreApi.Tests`, and `BioTwin_AI.DotNetShared.Tests`.
- Adjust project references:
  - `BioTwin_AI.BlazorClient` -> `BioTwin_AI.DotNetShared`
  - `BioTwin_AI.AspNetCoreApi` -> `BioTwin_AI.DotNetShared`
  - `BioTwin_AI.BlazorClient.Tests` -> `BioTwin_AI.BlazorClient`/`BioTwin_AI.DotNetShared`
  - `BioTwin_AI.AspNetCoreApi.Tests` -> `BioTwin_AI.AspNetCoreApi`/`BioTwin_AI.DotNetShared`
  - `BioTwin_AI.DotNetShared.Tests` -> `BioTwin_AI.DotNetShared`
- Keep the new backend independently runnable while preserving the existing `BioTwin_AI` project as the runnable baseline.

#### Phase 3: API facade

- Re-implement all current service capabilities in `BioTwin_AI.AspNetCoreApi`.
- Add API endpoints for auth, resume, chat, RAG, Markdown refinement, PDF export, and health.
- Add API tests.
- Standardize error responses with `ProblemDetails`.
- Add a health endpoint.

#### Phase 4: Client migration

- Redesign layout, routes, localization, and CSS/theme tokens.
- Integrate Microsoft Fluent UI Blazor and complete a light/dark theme switching spike.
- Implement WASM equivalents for all old pages using the new technical geek personal-site style.
- Implement resume list/upload/edit/export workflow.
- Implement chat/RAG workflow.
- Replace service injection with typed HTTP clients.

#### Phase 5: Local orchestration

- Update `start-local.ps1` to run both API and WASM frontend.
- Update Dockerfile or add a compose profile for API-only backend execution.
- Update README with local development commands.

#### Phase 6: Cloudflare readiness

- Add Blazor WASM publish scripts, for example `build-cloudflare-pages.ps1` and `build-cloudflare-pages.sh`.
- Document Cloudflare Pages or Workers Static Assets setup.
- Verify SPA fallback and base href.
- Verify API base URL is injected through environment configuration.

### 10. Acceptance Criteria

Milestone 1 is complete when:

- The frontend runs as a standalone Blazor WebAssembly application.
- The backend runs as a standalone ASP.NET Core Web API.
- `BioTwin_AI.AspNetCoreApi` fully covers all current service capabilities and does not depend on an old service compatibility layer.
- `BioTwin_AI.BlazorClient` covers every old application page and user workflow with the new technical geek personal-site style.
- The frontend supports light/dark theme switching and persists the user's choice.
- Core workflows work through the WASM frontend and HTTP API: resume upload, view, edit, export, chat, and RAG search.
- The client does not reference backend services, EF DbContext, RAG service, AI client, or secrets.
- Backend API tests cover the core endpoints.
- Local development scripts can start both frontend and backend.
- Frontend release publish output is ready for Cloudflare Pages or Workers Static Assets.
- Documentation clearly states which parts still depend on the non-Cloudflare backend and which parts are planned for later milestones.

### 11. Confirmed Decisions and Remaining Risks

1. Long-term backend target: the current path is Cloudflare Containers as the transition host for the ASP.NET Core API. Later milestones will introduce a new project and gradually rewrite the backend as Workers APIs.
2. Authentication: Milestone 1 keeps the local account system. The database design must leave room for future Cloudflare Access, Turnstile, or external identity provider integration.
3. Data migration: Milestone 1 keeps EF Core + local SQLite. Migration to Cloudflare D1, R2, and Vectorize is deferred to later milestones.
4. File upload: Milestone 1 keeps the current upload flow. Milestone 2 should revisit this for an R2-oriented upload model.
5. Embedding: Milestone 1 keeps the current local BGE-M3 ONNX embedding code unchanged.
6. PDF export: Milestone 1 keeps QuestPDF unchanged. A later milestone will evaluate how to move or replace it for Cloudflare Containers or other targets.
7. Localization: Milestone 1 keeps `.resx`. Blazor localization uses the .NET Resources system, and Blazor apps support `IStringLocalizer`/`IStringLocalizer<T>`. Cloudflare Pages only hosts the static Blazor WASM publish output, so there is no clear requirement to switch to JSON localization in Milestone 1. If a future Workers-native frontend or edge rendering target creates compatibility issues, JSON localization should be designed separately.

### 12. Confirmed Milestone 1 Implementation Details

The following details are confirmed. The implementation plan should use them directly:

1. `BioTwin_AI.AspNetCoreApi` must implement all current service capabilities in the first phase. Do not preserve compatibility with old services, and do not reuse old code through adapters. Feature behavior may reference the old implementation, but the code is new.
2. `BioTwin_AI.BlazorClient` must implement every old application page in the first phase. The UI must be redesigned toward a technical geek personal-site style, fully present skills, include theme switching, and explicitly use Microsoft Fluent UI Blazor as the third-party UI component library.
3. The local account schema must reserve mainstream external identity provider fields in Milestone 1, including but not limited to GitHub, Google, Microsoft, and Cloudflare Access.


---

## References

- Cloudflare Pages Blazor guide: https://developers.cloudflare.com/pages/framework-guides/deploy-a-blazor-site/
- Cloudflare Workers Static Assets SPA routing: https://developers.cloudflare.com/workers/static-assets/
- Cloudflare Containers overview: https://developers.cloudflare.com/containers/
- Cloudflare D1 overview: https://developers.cloudflare.com/d1/
- Cloudflare R2 overview: https://developers.cloudflare.com/r2/
- Cloudflare Vectorize overview: https://developers.cloudflare.com/vectorize/
- Cloudflare Workers AI overview: https://developers.cloudflare.com/workers-ai/
- Microsoft Blazor globalization and localization: https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization
- Microsoft Fluent UI Blazor: https://www.fluentui-blazor.net/
