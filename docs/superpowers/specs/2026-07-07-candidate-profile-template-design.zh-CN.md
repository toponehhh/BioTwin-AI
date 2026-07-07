# 候选人资料模板、分享链接、角色与时间线设计

日期：2026-07-07

## 目的

BioTwin_AI 需要一套可复用的候选人资料模板，用于展示任意 candidate 的公开信息。匿名访问者只能通过分享链接查看单个 candidate。如果 interviewer 需要浏览多个 candidate，则必须登录后进入受保护页面。

同一套模板必须支持：

- URL 没有 query string 时展示 default candidate。
- URL 包含 `uid=<profile-hash>` 时展示指定 candidate。
- 参考 `My career & experience` 的纵向 career timeline。
- 参考 `My Work` 的横向 work timeline。

## 核心决策

使用 profile hash 作为不透明的公开分享 token，而不是用户名、用户 ID 或简历内容的确定性 hash。

字段可以命名为 `ProfileHash`，以匹配 URL 里的 `uid=<hash>` 形式，但它的值应生成为较短的密码学安全随机分享码。它不能由 username、user ID 或简历内容推导出来。这样既能避免别人根据用户名或自增 ID 猜出公开资料链接，也方便用户分享。

分享码需要短到可以手工输入。建议使用类似 Crockford Base32 的大写、低混淆字符集，并排除 `0`、`O`、`1`、`I`、`L` 等容易混淆的字符。生产默认长度应为 8 位。可以把 6 位作为可配置下限，但匿名公开访问场景推荐默认使用 8 位。

公开 URL 格式为：

```text
/?uid=<current-profile-code>
```

示例：

```text
/?uid=K7X4Q9MF
```

当 `uid` 缺失时，页面展示 default candidate。当 `uid` 存在时，页面先把该值规范化为标准大写格式，再解析当前 `ProfileHash` 与该值匹配的 candidate。

## 数据库结构

在 `UserAccounts` 中增加 candidate 资料分享相关元数据：

```text
ProfileHash text unique not null
ProfileHashUpdatedAt datetime not null
CandidateProfileVersion integer not null default 1
IsProfilePublic integer not null default 1
IsDefaultCandidate integer not null default 0
```

增加唯一 partial index，确保一个数据库中只有一个未删除用户可以作为 default candidate：

```text
unique where IsDefaultCandidate = 1 and IsDeleted = 0
```

将 role 改为多对多表：

```text
UserRoles
- Id integer primary key
- UserId integer not null
- Role text not null
- CreatedAt datetime not null
- unique(UserId, Role)
```

继续使用现有 `UserRole` enum 值作为 role 名称：

```text
Candidate
Interviewer
Admin
```

现有单 role 数据需要迁移到 `UserRoles`。旧的 `UserAccounts.Role` 字段可以在迁移期间暂时保留，但新的授权检查应从 `UserRoles` 读取。

## 生成式资料展示数据存储

Timeline 和其他用于展示用户信息的数据，应由系统根据 candidate 的简历内容调用本地大模型抽取为结构化 JSON，然后应用到公开 candidate profile 模板上。

用户必须可以手工修正这些生成数据。大模型生成的数据和用户手工修正的数据都不应覆盖旧值，而应以版本形式保存。每一种信息类型的最新版本作为当前渲染来源。

所有半结构化展示数据可以放在同一张表中：

```text
CandidateProfileInfos
- Id integer primary key
- UserId integer not null
- InfoType text not null
- Version integer not null
- JsonData text not null
- Source text not null
- SourceResumeVersion integer null
- BasedOnInfoId integer null
- ModelName text null
- PromptVersion text null
- IsCurrent integer not null default 0
- CreatedAt datetime not null
- UpdatedAt datetime not null
- CreatedByUserId integer null
- unique(UserId, InfoType, Version)
```

`InfoType` 用来表示这条记录对应哪一种模板数据。第一版至少包含：

```text
careertimeline
worktimeline
```

这张表需要允许未来扩展更多类型，例如：

```text
skillmatrix
educationtimeline
projectgallery
profilesummary
```

`Source` 用来表示该版本如何产生：

```text
llm_generated
manual_edit
admin_edit
imported
```

当 candidate 修改简历内容时，系统应读取每个受影响 `InfoType` 当前最新的 `CandidateProfileInfos` 版本，并把这些 JSON 作为参考上下文传给大模型。大模型基于上一版结果生成下一版，而不是每次都从零开始。这样可以保留用户手工修正过的内容，也能让模型在多次简历修改之间保持连续性。

用户手工修正时，应创建一个新版本，`Source = manual_edit`，`BasedOnInfoId` 指向被修正的生成版本或上一版本，并将新版本设为 `IsCurrent = true`。旧版本继续保留，用于追踪和之后的大模型参考。

## Profile Hash 轮换

`ProfileHash` 是当前版本的分享 token。只要 candidate 修改了任何会出现在公开资料模板中的内容，就必须更新该值。

公开资料内容包括：

- 显示名称、昵称、头像和公开 headline。
- 公开 summary 或 bio。
- 当前 `CandidateProfileInfos` 记录，例如 `careertimeline`、`worktimeline`，以及未来的 `skillmatrix` 等数据。
- 当技能、教育经历、证书和可见简历 section 被直接渲染，或由当前 `CandidateProfileInfos` 记录表示时，也属于公开资料内容。
- 如果模板包含公开联系方式，也包括这些字段。

不应触发 hash 轮换的变更：

- 密码变更。
- 登录元数据。
- role 变更。
- default candidate 设置变更。
- 仅管理员可见的备注。
- 不显示在公开模板中的内部状态字段。

轮换应和 resume/profile 保存发生在同一个事务里：

```text
CandidateProfileVersion += 1
ProfileHash = GenerateRandomProfileHash()
ProfileHashUpdatedAt = now
```

当简历变更触发大模型抽取时，公开链接也应在新的展示数据被分享前失效。用户检查或接受生成的 JSON 后，当前 `CandidateProfileInfos` 版本才成为新分享 profile 的渲染来源。

旧匿名链接立即失效。匿名用户应看到通用的不可用或链接已过期状态。响应不应泄露用户是否存在。

## 公开访问与登录访问

匿名公开访问的能力应保持非常窄：

```text
GET /api/public/candidate-profile?uid=<profile-hash>
```

行为：

- 如果 `uid` 存在，则按当前 `UserAccounts.ProfileHash` 解析。
- 查询前将 `uid` 规范化为标准大写分享码格式。
- 如果 `uid` 缺失，则解析 default candidate。
- 要求 `IsProfilePublic = true`。
- 要求该用户拥有 `Candidate` role。
- 要求该用户处于 active 且未删除状态。
- 如果无法展示 profile，则返回 404 或通用不可用响应。
- 对匿名查询接口做限流，降低短分享码被猜测的风险。

已登录 interviewer 的访问路径单独设计：

```text
GET /api/interviewer/candidates
GET /api/interviewer/candidates/{id}
```

行为：

- 要求登录。
- 要求 `Interviewer` 或 `Admin` role。
- 允许浏览、搜索和切换多个 candidate。
- 不依赖公开 `ProfileHash`。
- 不会因为 candidate 轮换公开分享链接而失效。

## Default Candidate 解析

当未提供 `uid` 时，按以下顺序解析 default candidate：

1. 未删除、`IsDefaultCandidate = true`、`IsProfilePublic = true`，且拥有 `Candidate` role 的用户。
2. 如果没有显式 default，则选择一个未删除、同时拥有 `Admin` 和 `Candidate` role 的用户。
3. 如果仍然没有可用用户，则返回通用空状态或不可用的公开 profile 状态。

这样可以保持公开首页稳定，同时允许管理员指定哪个 candidate 代表默认站点体验。

## Candidate Profile 模板 DTO

前端应使用同一套共享模板来渲染 default profile 和分享链接 profile。

后端应基于每个 `InfoType` 当前最新的 `CandidateProfileInfos` 记录，加上 candidate 账号/profile 元数据，组装这个 DTO。

```text
CandidateProfileDto
- Candidate
  - DisplayName
  - Headline
  - Avatar
  - Summary
  - ProfileHashUpdatedAt
  - CandidateProfileVersion
- CareerTimelineItems
  - Title
  - Subtitle
  - PeriodLabel
  - SortOrder
  - Description
  - IsHighlighted
- WorkTimelineItems
  - Number
  - Title
  - Description
  - ImageUrl
  - LinkUrl
  - SortOrder
- Skills
- Education
- Certifications
- PublicContact
```

API 不应暴露 username、password 字段、删除元数据或内部管理员备注。

## Timeline UI 方向

模板包含两个 timeline section。

Career timeline 是纵向布局：

- 深色背景，带紫色重点光效。
- 大号居中标题。
- 左列展示职位标题和类别。
- 中间列展示年份或时间段标签。
- 中央发光竖线，并带当前/高亮指示点。
- 右列展示描述文本。
- 较晚或较弱的信息可以轻微淡出。

Work timeline 是横向布局：

- 深色 grid 或 rail 布局。
- work item 使用 `01`、`02`、`03` 等编号。
- 包含项目预览媒体、标题和摘要。
- 小屏幕下支持横向滚动。
- default candidate 和分享链接 candidate 使用同一个数据契约。

## 管理员与 Candidate 管理

管理员用户管理应包含：

- 为一个用户分配多个 role。
- 设置且仅设置一个 default candidate。
- 开关 candidate profile 是否公开。
- 复制 candidate 当前的公开分享 URL。
- 必要时手动重新生成 `ProfileHash`。

Candidate 编辑简历时，只要公开 profile 内容成功保存，就应自动重新生成 `ProfileHash`。保存后 UI 应显示新的分享 URL，让 candidate 明确知道旧链接已经失效。

## 测试策略

后端测试：

- 将现有 `UserAccounts.Role` 值迁移到 `UserRoles`。
- 强制一个数据库只能有一个 default candidate。
- 生成指定长度的短且唯一的 `ProfileHash`。
- 查询前规范化 `uid` 大小写。
- 生成 `ProfileHash` 发生碰撞时自动重试。
- 将大模型抽取出的展示 JSON 存入 `CandidateProfileInfos`。
- 用户手工修正时创建新的 `CandidateProfileInfos` 版本。
- 简历修改后重新生成时，把当前最新版本作为参考传给大模型。
- 在没有 `uid` 时解析 default candidate。
- 按当前 `ProfileHash` 解析 candidate。
- 简历修改后拒绝旧的 `ProfileHash`。
- 拒绝已删除、非公开或非 candidate 用户。
- 允许已登录 interviewer 在不依赖 `ProfileHash` 的情况下列出多个 candidate。

前端测试：

- 公开页面在 default candidate 场景下调用不带 `uid` 的 profile API。
- 当 query string 中存在 `uid` 时，公开页面将其传给 API。
- 共享模板为 default candidate 和指定 candidate 渲染相同 section。
- 匿名用户看不到 interviewer 的 candidate 导航。
- 已登录 interviewer 可以访问多 candidate 视图。

视觉验证：

- 捕获公开 candidate 模板的桌面和移动端截图。
- 验证纵向 career timeline 和横向 work timeline 不发生重叠或文字裁切。
- 验证浮动顶部菜单在 profile 页面中仍然可用。

## 实现备注

在应用代码中使用 `RandomNumberGenerator` 生成 profile hash，然后使用低混淆的大写字符集编码。生产默认长度应为 8 位。发布新 code 前必须通过数据库唯一索引检查，发生碰撞时重新生成。

因为分享码刻意设计得较短，公开查询接口应包含限流和通用错误响应。这个 code 是公开定位符，不是认证凭据。

Hash 轮换逻辑应集中放在 candidate profile service 中，避免未来新增 resume-edit endpoint 时忘记让旧公开链接失效。

除非未来出现审计需求，第一版不应存储历史公开 hash。第一版中只有当前 `ProfileHash` 有效。

不同 `InfoType` 的展示 JSON 应有各自的 schema 校验。只有校验通过的版本才能被标记为 current；无效的大模型输出应仅作为失败生成记录保存，或在发布前被拒绝。
