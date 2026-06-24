# Milestone 1 计划：面向 Cloudflare 迁移的前后端分离

> English version: `milestone-1-frontend-backend-separation.en.md`


### 1. 背景与总目标

BioTwin_AI 当前是一个 ASP.NET Core + Blazor Server/Razor Components 混合在同一个 Web 项目中的应用。长期目标是将整个系统迁移到 Cloudflare 平台，尽量使用 Cloudflare 资源承载前端、API、数据、对象存储、向量检索和 AI 能力。

第一个 milestone 不直接完成全部 Cloudflare 迁移，而是先做最关键的架构拆分：

- 前端独立为 Blazor WebAssembly 项目，后续可部署到 Cloudflare Pages 或 Workers Static Assets。
- 后端暂时保持 ASP.NET Core Web API 技术栈，先把原来的页面交互能力变成清晰的 HTTP API。
- 功能目标与当前应用保持一致，但新项目代码完全新写；旧代码只能作为功能参考，不作为兼容层或直接复制目标。
- 为后续 milestone 迁移到 Cloudflare D1、R2、Vectorize、Workers AI、Containers 或 Workers API 做边界准备。
- 当前已有代码和文件夹先全部保持不动；本 milestone 通过新建项目完成拆分，避免在第一步重构中破坏现有可运行基线。

### 2. Cloudflare 约束与迁移判断

根据 Cloudflare 官方文档：

- Cloudflare Pages 明确支持部署 Blazor WebAssembly，Blazor Server 与 Cloudflare edge network model 不兼容；因此前端应迁到 Blazor WASM。
- Cloudflare Workers Static Assets 支持 SPA fallback，可用于把非静态资源请求回退到 `index.html`，适合 Blazor WASM 路由。
- Cloudflare D1 是托管 serverless SQLite 语义数据库，可从 Workers 和 Pages 项目访问。
- Cloudflare R2 适合放简历原始文件、导出 PDF、模型文件或其它大对象。
- Cloudflare Vectorize 可作为后续替代本地 sqlite/vector payload 的向量数据库。
- Cloudflare Workers AI 可从 Workers、Pages 或 API 调用，用于后续替代本地/外部模型调用。
- Cloudflare Containers 可以通过 Worker 路由到容器实例，后续可作为 ASP.NET Core API 过渡运行方案，但这不是 milestone 1 的实施范围。

因此 milestone 1 的策略是：先拆边界，不急着把 ASP.NET Core 后端强行塞进 Workers。后端先保持可用，可继续 Docker/本地运行；后续再决策是迁到 Cloudflare Containers，还是逐步重写为 Workers API。

### 3. Milestone 1 范围

#### 包含

- 新建独立前端项目 `src/BioTwin_AI.BlazorClient`。
- 在 `BioTwin_AI.BlazorClient` 中实现当前应用的所有页面和用户流程，旧页面只作为功能清单参考。
- 重新设计前端 UI，风格倾向技术极客个人站点，能够全面展示当前技能、项目、简历和 AI/RAG 能力。
- 添加主题切换功能，至少支持 light/dark 两套主题，并保留扩展更多主题 token 的空间。
- 新建后端 API 项目 `src/BioTwin_AI.AspNetCoreApi`，技术栈暂时保持 ASP.NET Core Web API。
- `BioTwin_AI.AspNetCoreApi` 第一阶段要完整实现当前所有 service 能力；不考虑兼容旧项目 service，不通过适配层复用旧代码，功能行为可以参考旧实现。
- 新建共享契约项目 `src/BioTwin_AI.DotNetShared`，用于 DTO、API contracts 和跨 .NET 项目的轻量共享常量。
- 保持现有 `src/BioTwin_AI`、现有资源目录、现有测试目录不移动、不重命名，作为迁移参考和回退基线。
- 将 UI 依赖的服务调用改成 typed HTTP clients。
- 将后端当前的页面入口、server interactive render mode、Blazor Server 相关依赖从 API 边界逐步移除。
- 定义 API DTO 和路由，避免客户端直接引用 EF entity 或服务内部模型。
- 配置 CORS、API base URL、静态前端构建输出和本地双进程开发脚本。
- 添加前后端构建和测试验证；所有测试项目均按对应项目名追加 `.Tests` 命名。

#### 不包含

- 不在本 milestone 迁移数据库到 Cloudflare D1。
- 不在本 milestone 迁移文件存储到 R2。
- 不在本 milestone 将 RAG 向量检索迁移到 Vectorize。
- 不在本 milestone 将本地 BGE-M3 ONNX embedding 替换为 Workers AI。
- 不在本 milestone 完成 Cloudflare Containers 生产部署。
- 不在本 milestone 引入付费 UI 组件库或付费设计工具。

### 4. 推荐目标结构

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

说明：

- `BioTwin_AI` 是现有应用，milestone 1 中不移动、不重命名、不作为新 API 项目原地改造。
- `BioTwin_AI.DotNetShared` 只放稳定 API contract，不放 EF Core、RAG、AI client、database 或 server-only 代码。
- `BioTwin_AI.BlazorClient` 只通过 HTTP API 与后端交互，不直接引用后端 services。
- `BioTwin_AI.AspNetCoreApi` 负责承接新的 Web API 边界，并按当前应用能力重新实现 server-side services；旧 `BioTwin_AI` 只作为行为参考。
- 项目名显式包含技术栈，是为了给未来使用其它技术栈重写 App 留出命名空间，例如未来可新增 `BioTwin_AI.WorkersApi` 或其它实现，而不与当前 .NET 实现混淆。
- 测试项目命名规则固定为 `<ProjectName>.Tests`。

### 5. API 边界草案

第一轮 API 先以当前 UI 工作流为准，不追求最终 Cloudflare-native 形态。

建议初始路由：

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

DTO 原则：

- 请求/响应对象放在 `BioTwin_AI.DotNetShared`。
- DTO 命名以 workflow 为中心，例如 `ResumeSummaryDto`、`ResumeDetailDto`、`ChatRequestDto`、`ChatResponseDto`。
- API 不返回 EF entity。
- 文件上传先保持 multipart/form-data，后续迁移 R2 时再改为 presigned/direct upload 模式。

### 6. 前端重建设计策略

M1 的前端目标不是简单搬运旧 UI，而是在新 `BioTwin_AI.BlazorClient` 中重建完整体验：

1. 创建空 Blazor WASM 项目，先跑通路由、layout、语言资源、CSS、主题 token 和 API base URL。
2. 建立新的信息架构：个人技术主页、技能展示、项目/经历展示、简历管理、Markdown 编辑、PDF 导出、AI chat/RAG 等当前所有页面能力都要有 WASM 版本。
3. UI 风格采用技术极客个人站点方向：更像 portfolio + AI workspace，而不是传统后台管理系统；视觉上可以使用终端感、代码感、技术栈标签、项目时间线、能力矩阵和深浅主题切换。
4. 添加主题切换功能。主题状态需要在浏览器本地持久化，刷新后保留；主题 token 应用于基础色、背景、文字、边框、代码/终端区域和交互状态。
5. 第三方 UI 控件库明确选择 Microsoft Fluent UI Blazor：https://www.fluentui-blazor.net/。
6. Fluent UI Blazor 的组件只作为基础控件层使用，整体视觉仍需要围绕“技术极客个人站点 + AI workspace”重新设计；不要直接套默认企业后台风格。
7. 所有旧页面只作为功能覆盖清单，不限制新 UI 布局、组件结构或视觉风格。
8. 保持现有 `BioTwin_AI` 项目的 server-rendered UI 临时可运行，直到 WASM 前端覆盖核心流程后再讨论是否停用。

如果当前组件强依赖 server services，先引入 client-side interface，例如：

```csharp
public interface IResumeApiClient
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default);
}
```

然后由 WASM 项目里的 HTTP implementation 实现它。

### 7. 后端重建策略

新后端 `BioTwin_AI.AspNetCoreApi` 在 milestone 1 中保留 ASP.NET Core：

- 新建 API 项目的 `Program.cs` 以 Web API host 为目标；不原地改造现有 `BioTwin_AI/Program.cs`。
- 第一阶段完整实现当前所有 service 功能，包括认证、session、简历上传/解析、Markdown refinement、PDF export、RAG、embedding、AI chat、数据库访问和必要的健康检查。
- 后端代码完全新写，不做旧 service 兼容层，不通过 adapter 调旧项目代码；旧实现只作为业务行为参考。
- 技术栈继续使用 Serilog、EF Core、SQLite、RAG、当前 embedding、AI chat、resume refinement、QuestPDF 等既有选择，降低 M1 的平台迁移变量。
- 添加 controllers 或 minimal APIs。建议先用 controllers，让接口分组和测试更清晰。
- 添加 CORS policy，开发时允许 WASM dev origin，生产时只允许 Cloudflare Pages domain。
- 将 auth/session 从 server-side scoped UI state 改造成 API-friendly session/token/cookie 模式。

认证是需要重点讨论的风险点：Blazor WASM 无法安全保存后端 secret。用户登录态应由后端签发 HttpOnly cookie 或短期 token，客户端不能持有 OpenRouter/API/database secrets。

本地账号 schema 在 M1 需要预留外部身份扩展。建议至少设计 `UserExternalIdentity` 或等价结构，字段包括：

- `UserId`
- `Provider`，例如 `GitHub`、`Google`、`Microsoft`、`CloudflareAccess`
- `ProviderUserId` 或 `Subject`
- `ProviderEmail`
- `ProviderEmailVerified`
- `ProviderDisplayName`
- `ProviderAvatarUrl`
- `LinkedAt`
- `LastLoginAt`
- `RawClaimsJson`，用于保留非敏感 claims 快照

默认不存储 OAuth access token/refresh token；如果未来确实需要存储，必须另行设计加密、轮换和最小权限策略。

### 8. Cloudflare 对齐点

Milestone 1 完成后，Cloudflare 迁移路径会更清晰：

- Frontend: Blazor WASM build output `wwwroot` 可部署到 Cloudflare Pages，或 Workers Static Assets。
- Backend short-term: ASP.NET Core API 可继续 Docker 部署；如果选 Cloudflare Containers，后续通过 Worker fetch proxy 到 container。
- Backend long-term: 将 API 按业务模块逐步迁移为 Workers。
- Database: SQLite/EF model 可作为 D1 schema migration 的输入。
- Files: 上传文件、PDF、模型文件可迁移到 R2。
- Vectors: 当前 RAG vector payload 可迁移到 Vectorize。
- AI: 当前在线 chat/本地 embedding 抽象可迁移到 Workers AI 或 AI Gateway。

### 9. 实施阶段建议

#### Phase 1: Inventory and contracts

- 列出现有页面、组件、服务依赖和用户流程。
- 将现有页面清单定义为 M1 必须覆盖范围，而不是迁移候选范围。
- 标记每个组件是 pure UI、client state、server API dependent 还是 server-only。
- 起草 `BioTwin_AI.DotNetShared` DTO。
- 定义 API route map。

#### Phase 2: Solution split

- 新建 `BioTwin_AI.BlazorClient` Blazor WASM 项目。
- 新建 `BioTwin_AI.AspNetCoreApi` ASP.NET Core Web API 项目。
- 新建 `BioTwin_AI.DotNetShared` class library。
- 新建对应测试项目：`BioTwin_AI.BlazorClient.Tests`、`BioTwin_AI.AspNetCoreApi.Tests`、`BioTwin_AI.DotNetShared.Tests`。
- 将 solution 引用关系调整为：
  - `BioTwin_AI.BlazorClient` -> `BioTwin_AI.DotNetShared`
  - `BioTwin_AI.AspNetCoreApi` -> `BioTwin_AI.DotNetShared`
  - `BioTwin_AI.BlazorClient.Tests` -> `BioTwin_AI.BlazorClient`/`BioTwin_AI.DotNetShared`
  - `BioTwin_AI.AspNetCoreApi.Tests` -> `BioTwin_AI.AspNetCoreApi`/`BioTwin_AI.DotNetShared`
  - `BioTwin_AI.DotNetShared.Tests` -> `BioTwin_AI.DotNetShared`
- 保持新后端可单独启动，同时保持现有 `BioTwin_AI` 项目仍可作为基线运行。

#### Phase 3: API facade

- 在 `BioTwin_AI.AspNetCoreApi` 中完整新写当前所有 service 能力。
- 为 auth、resume、chat、RAG、markdown refinement、PDF export、health 添加 API endpoints。
- 添加 API tests。
- 明确错误响应格式，例如 `ProblemDetails`。
- 添加 health endpoint。

#### Phase 4: Client migration

- 重建设计 layout、routes、localization、CSS/theme tokens。
- 集成 Microsoft Fluent UI Blazor，并完成 light/dark 主题切换 spike。
- 实现所有旧页面的 WASM 等价功能，同时使用新的技术极客个人站点风格。
- 实现 resume list/upload/edit/export workflow。
- 实现 chat/RAG workflow。
- 将服务注入改成 typed HTTP clients。

#### Phase 5: Local orchestration

- 更新 `start-local.ps1`，同时启动 API 和 WASM frontend。
- 更新 Dockerfile 或新增 compose profile，用于 API-only backend。
- 添加 README 中的本地开发命令。

#### Phase 6: Cloudflare readiness

- 添加 Blazor WASM publish script，例如 `build-cloudflare-pages.ps1` 和 `build-cloudflare-pages.sh`。
- 添加 Cloudflare Pages/Workers Static Assets notes。
- 确认 SPA fallback 和 base href。
- 确认 API base URL 通过环境配置注入。

### 10. 验收标准

Milestone 1 完成时应满足：

- 前端可以作为独立 Blazor WASM 应用启动。
- 后端可以作为独立 ASP.NET Core Web API 启动。
- `BioTwin_AI.AspNetCoreApi` 完整覆盖当前所有 service 功能，且不依赖旧项目 service 兼容层。
- `BioTwin_AI.BlazorClient` 覆盖旧应用全部页面和用户流程，并采用新的技术极客个人站点风格。
- 前端支持 light/dark 主题切换，并能持久化用户选择。
- 简历上传、查看、编辑、导出、chat/RAG 等流程可通过 WASM 前端调用 API 完成。
- 客户端不引用后端 service、EF DbContext、RAG service、AI client 或 secrets。
- 后端 API tests 覆盖核心接口。
- 本地开发脚本可以一键启动前后端。
- 前端 release publish 输出可作为 Cloudflare Pages/Workers Static Assets 输入。
- 文档明确哪些功能仍依赖非 Cloudflare 后端，哪些在后续 milestone 迁移。

### 11. 已确认决策与后续风险

1. 后端长期落点：当前路线是先用 Cloudflare Containers 作为 ASP.NET Core API 的过渡承载方式；之后会新建项目逐步重写为 Workers API。
2. 登录态：M1 继续使用本地账号体系；但数据库设计需要预留扩展空间，后续可能接入 Cloudflare Access、Turnstile 或外部身份提供商。
3. 数据迁移：M1 继续使用 EF Core + 本地 SQLite；迁移到 Cloudflare D1、R2、Vectorize 的工作放到后续 milestone。
4. 文件上传：M1 先保持现有上传方式不变；M2 再考虑改成面向 R2 的新上传流程。
5. Embedding：M1 先保持当前本地 BGE-M3 ONNX embedding 代码不变。
6. PDF export：M1 先保持 QuestPDF 不变；后续 milestone 再评估它在 Cloudflare Containers 或其它目标上的迁移方式。
7. 多语言资源：M1 先保持 `.resx`。Blazor localization 使用 .NET Resources system，Blazor apps 支持 `IStringLocalizer`/`IStringLocalizer<T>`；Cloudflare Pages 只是托管 Blazor WASM 的静态发布输出，因此当前没有明确证据要求为了 Pages 部署在 M1 改成 JSON localization。后续如果迁移到 Workers-native 前端/边缘渲染时出现兼容性问题，再单独设计 JSON localization。

### 12. 已确认的 M1 实施细节

以下细节已确认，后续 implementation plan 应直接按这些约束展开：

1. `BioTwin_AI.AspNetCoreApi` 第一阶段就要实现当前所有 service 功能；不兼容旧 service，不通过适配层复用旧代码，功能可以参考旧实现但代码完全新写。
2. `BioTwin_AI.BlazorClient` 第一阶段就要实现旧应用所有页面；UI 需要重做，方向是技术极客个人站点，全面展示技能，并加入主题切换功能；第三方 UI 控件库明确使用 Microsoft Fluent UI Blazor。
3. 本地账号体系需要在 M1 schema 中预留主流外部身份 provider 扩展字段，包括但不限于 GitHub、Google、Microsoft 和 Cloudflare Access。

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
