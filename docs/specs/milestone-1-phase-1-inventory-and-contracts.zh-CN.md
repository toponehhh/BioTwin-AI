# Milestone 1 Phase 1 规格：现状盘点与新架构契约

> English version: `milestone-1-phase-1-inventory-and-contracts.en.md`

## 1. 目标

Phase 1 不创建新代码项目，也不移动现有代码。它的产出是把当前 `src/BioTwin_AI` 的页面、服务、数据模型和用户流程扫描成可执行规格，作为后续 Phase 2 新建项目和 Phase 3/4 实现的边界。

本阶段确认：

- 现有 `src/BioTwin_AI` 继续保持不动，作为行为参考和回退基线。
- 后续新建 `src/BioTwin_AI.BlazorClient`、`src/BioTwin_AI.AspNetCoreApi`、`src/BioTwin_AI.DotNetShared`。
- 所有新测试项目使用 `<ProjectName>.Tests` 命名。
- 新 API 和新 WASM 客户端完全新写；旧服务和旧页面只作为行为参考，不作为兼容层或适配层调用目标。
- 前端 UI 使用 Microsoft Fluent UI Blazor 作为基础组件库，但整体视觉方向是技术极客个人站点 + AI workspace。

## 2. 当前系统概览

当前应用是单一 ASP.NET Core Web 项目：

- Host: `src/BioTwin_AI/Program.cs`
- UI: Blazor Server / Razor Components，`InteractiveServer`
- 数据访问: EF Core + SQLite，数据库文件位于运行目录下的 `database/biotwin.db`
- 日志: Serilog
- AI chat: `Microsoft.Extensions.AI` + OpenAI-compatible client
- Embedding: 本地 BGE-M3 ONNX，通过 `Microsoft.ML.OnnxRuntime.Extensions`
- RAG: 当前本地服务和数据库向量 payload
- PDF: QuestPDF
- 简历转换: `ResumeUploadService` 调用 All2MD HTTP 服务
- 多语言: `.resx` + `IStringLocalizer`

当前启动流程中会执行：

1. 创建 SQLite 目录和 DbContext。
2. 注册 Razor Components 和 server interactive render mode。
3. 注册 AI、embedding、RAG、auth、resume、PDF 等 scoped/singleton 服务。
4. `EnsureCreatedAsync()` 初始化数据库。
5. `IRagService.InitializeAsync()` 初始化 RAG 向量索引。
6. 映射 `/culture/set` 和 Razor Components。

## 3. 当前页面与用户流程清单

M1 新前端必须覆盖以下页面和流程。旧路由可以作为迁移兼容入口，但新 UI 不需要复制旧布局。

| 当前路由 | 当前文件 | 当前依赖 | M1 覆盖要求 |
| --- | --- | --- | --- |
| `/`, `/chat` | `Components/Pages/Index.razor` | `CurrentUserSession`, `AuthService`, `IJSRuntime`, `ChatInterfaceComponent` | 新首页可重构为个人技术主页 + AI workspace 入口；必须保留登录态检查和 chat 工作流入口。 |
| `/login` | `Components/Pages/Login.razor` | `AuthService`, `CurrentUserSession`, `IJSRuntime` | 支持登录、注册、interviewer 模式；为未来外部 provider 登录预留 UI extension point。 |
| `/resume/upload` | `Components/Pages/ResumeUpload.razor` | `ResumeUploadService`, `AuthService`, `CurrentUserSession`, `IJSRuntime` | 支持文件选择、转换 Markdown、重复文件识别、保存 sections、重建 embeddings。 |
| `/resume/edit` | `Components/Pages/ResumeEdit.razor` | `BioTwinDbContext`, `IRagService`, `ResumeUploadService`, session/auth, JS | 必须从直接 DbContext 操作迁移为 API 驱动；M1 只保留整份 Markdown 编辑和保存。section tree 只用于结构预览和导航，不提供 section 单独编辑、复制或删除。 |
| `/resume/export` | `Components/Pages/ResumeExport.razor` | `BioTwinDbContext`, `ResumePdfExportService`, session/auth, JS | 支持选择简历、预览 Markdown、复制、下载 Markdown、下载 PDF、下载原始文件。 |
| `/home` | `Components/Pages/Home.razor` | localizer | 可合并到新个人主页，但功能入口不能消失。 |
| `/counter` | `Components/Pages/Counter.razor` | localizer | 示例页，不属于业务功能；M1 可不作为生产导航入口。 |
| `/Error` | `Components/Pages/Error.razor` | diagnostics, localizer | API/WASM 分离后改为前端错误页 + API `ProblemDetails`。 |
| `/not-found` | `Components/Pages/NotFound.razor` | localizer | WASM 保留 404 体验，Cloudflare SPA fallback 需要覆盖。 |

## 4. 当前组件清单

| 组件 | 当前职责 | M1 组件类别 | M1 处理方式 |
| --- | --- | --- | --- |
| `ChatInterfaceComponent` | 提问、流式回答、RAG 引用展示、消息列表 | server API dependent | 改为调用 typed `IChatApiClient`；流式返回使用 NDJSON。 |
| `MarkdownEditor` | EasyMDE wrapper、编辑器值同步、定位行、JS interop | client UI + JS interop | 可保留功能概念；WASM 中重新封装，避免 server-only 假设。 |
| `ResumeSidebarComponent` | 加载 resume sections 并展示侧边栏 | server API dependent | 改为 `IResumeApiClient` 获取 section tree。 |
| `NavMenu` | 导航、登录态显示、登出、折叠状态 | client state + API dependent | 迁移为 Fluent UI 导航；登出调用 API，主题切换也放入顶栏/侧栏。 |
| `MainLayout` | 应用外壳、语言切换、重连提示 | client layout | 新 WASM layout 重做，语言切换和主题切换都应在 layout 层可达。 |
| `ReconnectModal` | Blazor Server 重连提示 | server-only | WASM 不再需要 server circuit 重连提示；可替换为 API offline banner。 |

## 5. 当前服务能力清单

`BioTwin_AI.AspNetCoreApi` 在 M1 必须从零实现下列能力。旧类名只用于标记行为参考。

| 当前服务 | 当前能力 | 新 API 模块 |
| --- | --- | --- |
| `AuthService` | 注册、登录、恢复 session、登出 | `AuthController` / `SessionController` |
| `CurrentUserSession` | server scoped 登录态、candidate/interviewer role、browser storage | 后端 HttpOnly cookie session + WASM `AuthenticationStateProvider` 或轻量 session store |
| `ResumeUploadService` | 文件转 Markdown、保存简历、保存/替换 sections、删除、重建 embeddings、查询 entries/sections | `ResumesController`, `ResumeImportService`, `ResumeIndexingService` |
| `ResumeExportMarkdownBuilder` | sections 转 Markdown、PDF title 提取 | `ResumeExportService` |
| `ResumePdfExportService` | Markdown 生成 PDF bytes | `ResumeExportController` |
| `ResumeMarkdownRefinementService` | 调用 AI 优化 Markdown | `ResumeRefinementController` / `ResumeRefinementService` |
| `EmbeddingService` + `BgeM3OnnxEmbeddingModel` | 本地 BGE-M3 embedding | `EmbeddingService`，保留本地 ONNX 实现 |
| `RagService` | 初始化索引、生成 payload、搜索、聊天上下文搜索、清空 | `RagController`, `RagIndexService`, `RagSearchService` |
| `AgentService` | 普通回答、流式回答、结构化 stream chunk | `ChatController`, `ChatOrchestrationService` |
| `AiClientServiceCollectionExtensions` | 注册 chat client 和配置 | API 项目中的 AI options + service registration |

## 6. 当前数据模型清单

当前 EF entity 不能直接暴露给 WASM 客户端。M1 API 应通过 DTO 映射。

| Entity | 当前字段/关系重点 | M1 处理 |
| --- | --- | --- |
| `UserAccount` | `Id`, `Username`, `PasswordHash`, `CreatedAt`; `Username` unique | 继续本地账号；新增持久化 `Role` 字段和外部身份扩展表。 |
| `ResumeEntry` | 标题、原文件名、原文件内容、content type、大小、hash、tenant、sections | API 返回 summary/detail DTO，不返回原文件内容，下载原文件使用单独 endpoint。 |
| `ResumeSection` | resume parent、section parent/children、heading level、title、content、sort order、tenant、vector | API 返回树状 section DTO；M1 不暴露 section mutation API，排序和父子关系由整份 Markdown 解析结果维护。 |
| `ResumeSectionVector` | section id、tenant、title、parent title、content、embedding payload | server-only；仅 RAG/search API 返回引用摘要。 |
| `ResumeSectionChunk` | section chunk metadata 和 payload | server-only 或 shared search result metadata，按需投影。 |

M1 新增建议实体：

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

默认不保存 OAuth access token 或 refresh token。如未来需要 token 存储，必须单独设计加密、轮换和最小权限策略。

## 7. 目标项目职责

```text
src/
  BioTwin_AI/               # 现有应用，M1 不移动、不重命名、不改造成新 API
  BioTwin_AI.BlazorClient/  # 新 Blazor WASM 前端
  BioTwin_AI.AspNetCoreApi/ # 新 ASP.NET Core Web API
  BioTwin_AI.DotNetShared/  # DTO、API contracts、共享 constants
tests/
  BioTwin_AI.Tests/
  BioTwin_AI.BlazorClient.Tests/
  BioTwin_AI.AspNetCoreApi.Tests/
  BioTwin_AI.DotNetShared.Tests/
```

引用关系：

- `BioTwin_AI.BlazorClient` -> `BioTwin_AI.DotNetShared`
- `BioTwin_AI.AspNetCoreApi` -> `BioTwin_AI.DotNetShared`
- `BioTwin_AI.BlazorClient.Tests` -> `BioTwin_AI.BlazorClient`, `BioTwin_AI.DotNetShared`
- `BioTwin_AI.AspNetCoreApi.Tests` -> `BioTwin_AI.AspNetCoreApi`, `BioTwin_AI.DotNetShared`
- `BioTwin_AI.DotNetShared.Tests` -> `BioTwin_AI.DotNetShared`

禁止关系：

- `BioTwin_AI.BlazorClient` 不引用 `BioTwin_AI.AspNetCoreApi`。
- `BioTwin_AI.BlazorClient` 不引用 EF Core entity、DbContext、RAG service、AI service、server secrets。
- `BioTwin_AI.DotNetShared` 不引用 EF Core、ASP.NET Core hosting、QuestPDF、ONNX Runtime、sqlite、AI client。
- `BioTwin_AI.AspNetCoreApi` 不通过 adapter 调用旧 `src/BioTwin_AI` 的 service 实现。

代码组织硬约束：

- `BioTwin_AI.AspNetCoreApi` 的业务 HTTP API 必须使用 MVC/Web API controller；不使用 Minimal API、route group 或在 `Program.cs` 中集中 `MapGet`/`MapPost` 实现业务接口。
- 每个功能域必须有独立 controller，例如 `AuthController`、`SessionController`、`ResumesController`、`ResumeExportController`、`ResumeRefinementController`、`ChatController`、`RagController`、`HealthController`。
- 每个 class、record、enum、interface 都必须放在自己的 `.cs` 文件中；不要创建 `Dtos.cs`、`Requests.cs`、`Responses.cs`、`Models.cs` 这类把同类型或多个类型堆在一起的文件。
- 可以通过文件夹按功能域组织类型，例如 `Contracts/Auth/LoginRequest.cs`、`Contracts/Auth/AuthResult.cs`，但不能把多个不同类型放在同一个文件里。

## 8. API 路由契约草案

API 默认返回 JSON；错误返回 `ProblemDetails`。M1 session 机制确定使用后端签发的 HttpOnly cookie；生产环境必须配合 HTTPS、`Secure=true`、合理的 `SameSite` 策略和受限 CORS。所有路由由对应 controller 暴露，不通过 Minimal API 实现。

### Auth and Session

| Method | Route | Request | Response | 说明 |
| --- | --- | --- | --- | --- |
| `GET` | `/api/session/current` | - | `CurrentSessionResponse` | 返回当前登录态、用户、角色、可用 provider。 |
| `POST` | `/api/auth/register` | `RegisterRequest` | `AuthResult` | 本地账号注册。 |
| `POST` | `/api/auth/login` | `LoginRequest` | `AuthResult` | 本地账号登录。 |
| `POST` | `/api/auth/interviewer-login` | - | `AuthResult` | 仅作为 dev/demo 快捷入口；生产权限以持久化用户角色为准。 |
| `POST` | `/api/auth/logout` | - | `NoContent` | 清理 server session/cookie。 |
| `GET` | `/api/auth/external/providers` | - | `ExternalIdentityProviderDto[]` | 先返回未来 provider 的展示状态，M1 可 disabled。 |

### Resumes

| Method | Route | Request | Response | 说明 |
| --- | --- | --- | --- | --- |
| `GET` | `/api/resumes` | query | `ResumeSummaryDto[]` | 简历列表。 |
| `GET` | `/api/resumes/{resumeId}` | - | `ResumeDetailDto` | 简历详情和 section tree。 |
| `POST` | `/api/resumes/upload/convert` | `multipart/form-data` | `ConvertedResumeFileDto` | 上传并转换为 Markdown，不保存。 |
| `POST` | `/api/resumes` | `SaveResumeMarkdownRequest` | `ResumeDetailDto` | 保存新简历并索引。 |
| `PUT` | `/api/resumes/{resumeId}/markdown` | `SaveResumeMarkdownRequest` | `ResumeDetailDto` | 用整份 Markdown 替换 sections 并重建索引。 |
| `DELETE` | `/api/resumes/{resumeId}` | - | `NoContent` | 删除简历及 sections/vectors。 |
| `POST` | `/api/resumes/rebuild-embeddings` | - | `RebuildEmbeddingsResponse` | 重建所有向量。 |
| `GET` | `/api/resumes/{resumeId}/original` | - | file | 下载原始上传文件。 |

### Resume Sections

M1 不提供独立的 section mutation API。section tree 通过 `GET /api/resumes/{resumeId}` 返回，前端只用于结构预览和导航。所有 section 变更都由 `PUT /api/resumes/{resumeId}/markdown` 接收整份 Markdown 后在服务端重新解析、保存并重建索引。

### Export and Refinement

| Method | Route | Request | Response | 说明 |
| --- | --- | --- | --- | --- |
| `GET` | `/api/resumes/{resumeId}/export/markdown` | - | `ResumeMarkdownExportDto` | 返回整份 Markdown。 |
| `GET` | `/api/resumes/{resumeId}/export/pdf` | - | file | 返回 PDF bytes。 |
| `POST` | `/api/resumes/refine` | `RefineMarkdownRequest` | `RefineMarkdownResponse` | 优化 Markdown。 |

### Chat and RAG

| Method | Route | Request | Response | 说明 |
| --- | --- | --- | --- | --- |
| `POST` | `/api/chat` | `ChatRequest` | `ChatResponse` | 非流式回答。 |
| `POST` | `/api/chat/stream` | `ChatRequest` | NDJSON `ChatStreamChunk` | 流式回答；每一行输出一个 JSON chunk。 |
| `POST` | `/api/rag/search` | `RagSearchRequest` | `RagSearchResponse` | 搜索简历上下文。 |
| `GET` | `/api/health` | - | `HealthResponse` | 健康检查。 |

## 9. `BioTwin_AI.DotNetShared` DTO 草案

建议 namespace：

- `BioTwin_AI.DotNetShared.Auth`
- `BioTwin_AI.DotNetShared.Resumes`
- `BioTwin_AI.DotNetShared.Chat`
- `BioTwin_AI.DotNetShared.Rag`
- `BioTwin_AI.DotNetShared.Common`

文件组织规则：

- 下面列出的每个 DTO/record 都应创建独立文件。
- 文件路径应按 namespace/功能域分组，例如 `Auth/LoginRequest.cs`、`Resumes/ResumeSummaryDto.cs`、`Chat/ChatRequest.cs`。
- `BioTwin_AI.DotNetShared` 不允许使用一个大文件集中保存所有 DTO，也不允许把 request、response、summary、detail 多个类型放在同一个文件中。

核心 DTO：

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

## 10. 新前端信息架构

`BioTwin_AI.BlazorClient` 第一阶段必须覆盖旧业务能力，同时重做为技术极客个人站点风格。

建议页面：

| 新路由 | 目标 |
| --- | --- |
| `/` | 技术个人主页：身份介绍、技能矩阵、项目入口、AI/RAG 入口、最近简历状态。 |
| `/skills` | 技能、技术栈、AI/Cloud/.NET/Cloudflare 迁移能力展示。 |
| `/projects` | 项目/经历时间线，可关联简历 sections。 |
| `/chat` | AI chat + RAG 引用工作台。 |
| `/resume` | 简历总览和最近版本。 |
| `/resume/upload` | 上传、转换、保存和重建索引。 |
| `/resume/edit/{resumeId?}` | 整份 Markdown 编辑和保存；section tree 只用于结构预览、定位和导航，不提供 section 单独编辑。 |
| `/resume/export/{resumeId?}` | Markdown/PDF/原文件导出。 |
| `/login` | 登录、注册、interviewer 入口、未来外部登录占位。 |
| `/settings` | 主题、语言、API endpoint、未来 provider link 管理。 |

UI 约束：

- 使用 Microsoft Fluent UI Blazor 控件。
- 至少支持 light/dark 主题，并持久化到 browser storage。
- 主题 token 覆盖背景、文本、边框、按钮、代码区、终端感区域、状态色。
- 不直接套默认企业后台风格；需要 portfolio + AI workspace 的设计语言。
- 旧页面仅是功能覆盖清单，不限制新布局。

## 11. 新后端模块边界

`BioTwin_AI.AspNetCoreApi` 建议分层：

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

关键规则：

- 后端业务 endpoint 必须全部落在 controller 中，禁止用 Minimal API 作为业务接口实现方式。
- 每个 controller 只负责一个功能域；不要创建巨型 `ApiController` 或把多个业务域混在一个 controller 中。
- Controllers 只处理 HTTP、validation、DTO 映射和 status codes。
- Application services 负责业务流程。
- Infrastructure 负责 EF Core、AI client、ONNX、QuestPDF、HTTP integrations。
- 所有 API 返回 DTO，不返回 EF entity。
- 所有 server secret 只留在 API 项目配置中。
- 每个 controller、service、options、entity、DTO、request、response、enum、interface 都应使用独立 `.cs` 文件，文件名与类型名一致。

## 12. 测试映射

现有 `tests/BioTwin_AI.Tests` 提供行为参考。M1 新测试应该拆到对应项目。

| 现有测试区域 | 新测试项目 |
| --- | --- |
| Auth/session tests | `BioTwin_AI.AspNetCoreApi.Tests` 和必要的 `BioTwin_AI.BlazorClient.Tests` session store tests |
| Agent/chat tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| RAG tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| Embedding tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| Resume upload/delete/refine/export tests | `BioTwin_AI.AspNetCoreApi.Tests` |
| DTO validation/serialization tests | `BioTwin_AI.DotNetShared.Tests` |
| Frontend route/API client/theme tests | `BioTwin_AI.BlazorClient.Tests` |
| Serilog/config/dependency tests | `BioTwin_AI.AspNetCoreApi.Tests` |

后续实现时至少需要：

- API integration tests 覆盖 auth、resume upload/save/edit/delete/export、chat、RAG search。
- DTO serialization tests 保证 WASM/API 契约稳定。
- Frontend tests 覆盖主题切换、路由权限、API client 错误处理。

## 13. Phase 1 产出与验收

本规格完成后，Phase 1 视为完成，满足：

- 已列出当前页面、组件、服务、数据模型和用户流程。
- 已定义新项目职责、引用关系和禁止依赖。
- 已定义 API route map 和 DTO 草案。
- 已明确前端新信息架构和 Fluent UI Blazor 使用约束。
- 已明确本地账号和外部身份 provider 扩展点。
- 已明确后续测试拆分方向。

Phase 1 不包含：

- 新建 `.csproj`。
- 移动或重命名现有代码。
- 实现 API controller。
- 实现 Blazor WASM UI。
- 修改数据库 migration。

## 14. Phase 2 前已确认决策

以下进入 Phase 2 前的确认点已经确定：

1. API session 机制使用后端签发的 HttpOnly cookie，不使用短期 bearer token 作为 M1 默认登录态方案。
2. `/api/chat/stream` 使用 NDJSON streaming，保留 `POST` 请求和 `ChatRequest` request body。
3. 新前端首页使用 `/` 个人主页，不把 `/chat` 作为首页；个人主页的具体内容、模块顺序和文案需要在前端设计阶段单独确认。
4. `interviewer` 不再只是临时 UI 状态；M1 在本地账号模型中持久化用户角色，初始角色至少包括 `Candidate`、`Interviewer`、`Admin`。API 端基于持久化角色做权限判断。
5. `ResumeEdit` 只保留整份 Markdown 编辑和保存功能；不提供 section 单独编辑、复制或删除。section tree 仅用于结构预览、定位和导航。
