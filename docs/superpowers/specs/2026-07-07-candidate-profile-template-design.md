# Candidate Profile Template, Sharing, Roles, and Timeline Design
# 候选人资料模板、分享链接、角色与时间线设计

Date: 2026-07-07

日期：2026-07-07

## Purpose / 目的

BioTwin_AI needs one reusable candidate profile template that can render any candidate's public-facing information. Anonymous visitors can view a single candidate only through a shared URL. Interviewers who need to browse multiple candidates must log in and use authenticated pages.

BioTwin_AI 需要一套可复用的候选人资料模板，用于展示任意 candidate 的公开信息。匿名访问者只能通过分享链接查看单个 candidate。如果 interviewer 需要浏览多个 candidate，则必须登录后进入受保护页面。

The same template must support:

同一套模板必须支持：

- The default candidate when no query string is provided.
- A specific candidate when the URL contains `uid=<profile-hash>`.
- The vertical career timeline inspired by `My career & experience`.
- The horizontal work timeline inspired by `My Work`.
- URL 没有 query string 时展示 default candidate。
- URL 包含 `uid=<profile-hash>` 时展示指定 candidate。
- 参考 `My career & experience` 的纵向 career timeline。
- 参考 `My Work` 的横向 work timeline。

## Key Decisions / 核心决策

Use a profile hash as an opaque public sharing token, not as a deterministic hash of username, user ID, or resume content.

使用 profile hash 作为不透明的公开分享 token，而不是用户名、用户 ID 或简历内容的确定性 hash。

The field can be named `ProfileHash` to match the URL shape, but its value should be generated with a cryptographically strong random token. This prevents guessing profile links from usernames or sequential IDs.

字段可以命名为 `ProfileHash`，以匹配 URL 里的 `uid=<hash>` 形式，但它的值应使用密码学安全的随机 token 生成。这样可以避免别人根据用户名或自增 ID 猜出公开资料链接。

The public URL format is:

公开 URL 格式为：

```text
/?uid=<current-profile-hash>
```

When `uid` is absent, the page shows the default candidate. When `uid` is present, the page resolves the candidate whose current `ProfileHash` matches the value.

当 `uid` 缺失时，页面展示 default candidate。当 `uid` 存在时，页面解析当前 `ProfileHash` 与该值匹配的 candidate。

## Database Shape / 数据库结构

Extend `UserAccounts` with candidate profile sharing metadata:

在 `UserAccounts` 中增加 candidate 资料分享相关元数据：

```text
ProfileHash text unique not null
ProfileHashUpdatedAt datetime not null
CandidateProfileVersion integer not null default 1
IsProfilePublic integer not null default 1
IsDefaultCandidate integer not null default 0
```

Add a unique partial index so only one non-deleted user can be the default candidate:

增加唯一 partial index，确保一个数据库中只有一个未删除用户可以作为 default candidate：

```text
unique where IsDefaultCandidate = 1 and IsDeleted = 0
```

Move roles to a many-to-many table:

将 role 改为多对多表：

```text
UserRoles
- Id integer primary key
- UserId integer not null
- Role text not null
- CreatedAt datetime not null
- unique(UserId, Role)
```

Keep the existing `UserRole` enum values as role names:

继续使用现有 `UserRole` enum 值作为 role 名称：

```text
Candidate
Interviewer
Admin
```

Existing single-role data should be migrated into `UserRoles`. The old `UserAccounts.Role` column can remain temporarily during migration, but new authorization checks should read from `UserRoles`.

现有单 role 数据需要迁移到 `UserRoles`。旧的 `UserAccounts.Role` 字段可以在迁移期间暂时保留，但新的授权检查应从 `UserRoles` 读取。

## Profile Hash Rotation / Profile Hash 轮换

`ProfileHash` is a current-version sharing token. It must change whenever the candidate changes any content that appears in the public profile template.

`ProfileHash` 是当前版本的分享 token。只要 candidate 修改了任何会出现在公开资料模板中的内容，就必须更新该值。

Public profile content includes:

公开资料内容包括：

- Display name, nickname, avatar, and public headline.
- Public summary or bio.
- Career timeline items.
- Work/project timeline items.
- Skills, education, certifications, and visible resume sections.
- Public contact fields if the template includes them.
- 显示名称、昵称、头像和公开 headline。
- 公开 summary 或 bio。
- Career timeline 项。
- Work/project timeline 项。
- 技能、教育经历、证书和可见简历 section。
- 如果模板包含公开联系方式，也包括这些字段。

Changes that should not rotate the hash:

不应触发 hash 轮换的变更：

- Password changes.
- Login metadata.
- Role changes.
- Default-candidate changes.
- Admin-only notes.
- Internal status fields that are not visible in the public template.
- 密码变更。
- 登录元数据。
- role 变更。
- default candidate 设置变更。
- 仅管理员可见的备注。
- 不显示在公开模板中的内部状态字段。

Rotation should happen in the same transaction as the resume/profile save:

轮换应和 resume/profile 保存发生在同一个事务里：

```text
CandidateProfileVersion += 1
ProfileHash = GenerateRandomProfileHash()
ProfileHashUpdatedAt = now
```

Old anonymous links become invalid immediately. Anonymous users should see a generic unavailable or expired-link state. The response should not reveal whether a user exists.

旧匿名链接立即失效。匿名用户应看到通用的不可用或链接已过期状态。响应不应泄露用户是否存在。

## Public and Authenticated Access / 公开访问与登录访问

Anonymous public access is intentionally narrow:

匿名公开访问的能力应保持非常窄：

```text
GET /api/public/candidate-profile?uid=<profile-hash>
```

Behavior:

行为：

- If `uid` is present, resolve by current `UserAccounts.ProfileHash`.
- If `uid` is absent, resolve the default candidate.
- Require `IsProfilePublic = true`.
- Require the user to have the `Candidate` role.
- Require the user to be active and not deleted.
- Return 404 or a generic unavailable response when no profile can be shown.
- 如果 `uid` 存在，则按当前 `UserAccounts.ProfileHash` 解析。
- 如果 `uid` 缺失，则解析 default candidate。
- 要求 `IsProfilePublic = true`。
- 要求该用户拥有 `Candidate` role。
- 要求该用户处于 active 且未删除状态。
- 如果无法展示 profile，则返回 404 或通用不可用响应。

Authenticated interviewer access is separate:

已登录 interviewer 的访问路径单独设计：

```text
GET /api/interviewer/candidates
GET /api/interviewer/candidates/{id}
```

Behavior:

行为：

- Requires login.
- Requires `Interviewer` or `Admin` role.
- Allows browsing, searching, and switching between multiple candidates.
- Does not depend on public `ProfileHash`.
- Does not invalidate when a candidate rotates their public sharing link.
- 要求登录。
- 要求 `Interviewer` 或 `Admin` role。
- 允许浏览、搜索和切换多个 candidate。
- 不依赖公开 `ProfileHash`。
- 不会因为 candidate 轮换公开分享链接而失效。

## Default Candidate Resolution / Default Candidate 解析

When no `uid` is supplied, resolve the default candidate in this order:

当未提供 `uid` 时，按以下顺序解析 default candidate：

1. A non-deleted user with `IsDefaultCandidate = true`, `IsProfilePublic = true`, and the `Candidate` role.
2. If no explicit default exists, a non-deleted admin user that also has the `Candidate` role.
3. If neither exists, return a generic empty or unavailable public profile state.
1. 未删除、`IsDefaultCandidate = true`、`IsProfilePublic = true`，且拥有 `Candidate` role 的用户。
2. 如果没有显式 default，则选择一个未删除、同时拥有 `Admin` 和 `Candidate` role 的用户。
3. 如果仍然没有可用用户，则返回通用空状态或不可用的公开 profile 状态。

This keeps the public home page stable while still letting administrators choose which candidate represents the default site experience.

这样可以保持公开首页稳定，同时允许管理员指定哪个 candidate 代表默认站点体验。

## Candidate Profile Template DTO / Candidate Profile 模板 DTO

The frontend should render one shared template for both default and shared-link profiles.

前端应使用同一套共享模板来渲染 default profile 和分享链接 profile。

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

The API should not expose username, password fields, deleted metadata, or internal admin notes.

API 不应暴露 username、password 字段、删除元数据或内部管理员备注。

## Timeline UI Direction / Timeline UI 方向

The template includes two timeline sections.

模板包含两个 timeline section。

The career timeline is vertical:

Career timeline 是纵向布局：

- Dark background with purple accent lighting.
- Large centered heading.
- Left column for role title and category.
- Middle column for year or period label.
- Center glowing vertical line with active indicator.
- Right column for description.
- Later or less prominent items can fade subtly.
- 深色背景，带紫色重点光效。
- 大号居中标题。
- 左列展示职位标题和类别。
- 中间列展示年份或时间段标签。
- 中央发光竖线，并带当前/高亮指示点。
- 右列展示描述文本。
- 较晚或较弱的信息可以轻微淡出。

The work timeline is horizontal:

Work timeline 是横向布局：

- Dark grid or rail layout.
- Numbered work items such as `01`, `02`, `03`.
- Project preview media plus title and summary.
- Horizontal scrolling on small screens.
- Same data contract for default and shared candidate profiles.
- 深色 grid 或 rail 布局。
- work item 使用 `01`、`02`、`03` 等编号。
- 包含项目预览媒体、标题和摘要。
- 小屏幕下支持横向滚动。
- default candidate 和分享链接 candidate 使用同一个数据契约。

## Admin and Candidate Management / 管理员与 Candidate 管理

Admin user management should include:

管理员用户管理应包含：

- Assigning multiple roles to a user.
- Setting exactly one default candidate.
- Toggling whether a candidate profile is public.
- Copying the candidate's current public share URL.
- Manually regenerating `ProfileHash` if needed.
- 为一个用户分配多个 role。
- 设置且仅设置一个 default candidate。
- 开关 candidate profile 是否公开。
- 复制 candidate 当前的公开分享 URL。
- 必要时手动重新生成 `ProfileHash`。

Candidate resume editing should automatically regenerate `ProfileHash` after every successful public-profile content save. The UI should show the new share URL after saving so the candidate knows the previous link expired.

Candidate 编辑简历时，只要公开 profile 内容成功保存，就应自动重新生成 `ProfileHash`。保存后 UI 应显示新的分享 URL，让 candidate 明确知道旧链接已经失效。

## Testing Strategy / 测试策略

Backend tests:

后端测试：

- Migrates existing `UserAccounts.Role` values into `UserRoles`.
- Enforces one default candidate.
- Resolves default candidate without `uid`.
- Resolves current candidate by `ProfileHash`.
- Rejects old `ProfileHash` after resume changes.
- Rejects deleted, private, or non-candidate users.
- Allows logged-in interviewers to list multiple candidates without `ProfileHash`.
- 将现有 `UserAccounts.Role` 值迁移到 `UserRoles`。
- 强制一个数据库只能有一个 default candidate。
- 在没有 `uid` 时解析 default candidate。
- 按当前 `ProfileHash` 解析 candidate。
- 简历修改后拒绝旧的 `ProfileHash`。
- 拒绝已删除、非公开或非 candidate 用户。
- 允许已登录 interviewer 在不依赖 `ProfileHash` 的情况下列出多个 candidate。

Frontend tests:

前端测试：

- Public page calls the profile API without `uid` for the default candidate.
- Public page passes `uid` from query string when present.
- Shared template renders the same sections for default and selected candidates.
- Anonymous users do not see interviewer candidate navigation.
- Logged-in interviewers can access multi-candidate views.
- 公开页面在 default candidate 场景下调用不带 `uid` 的 profile API。
- 当 query string 中存在 `uid` 时，公开页面将其传给 API。
- 共享模板为 default candidate 和指定 candidate 渲染相同 section。
- 匿名用户看不到 interviewer 的 candidate 导航。
- 已登录 interviewer 可以访问多 candidate 视图。

Visual verification:

视觉验证：

- Capture desktop and mobile screenshots for the public candidate template.
- Verify the vertical career timeline and horizontal work timeline do not overlap or clip text.
- Verify the floating top menu remains usable on profile pages.
- 捕获公开 candidate 模板的桌面和移动端截图。
- 验证纵向 career timeline 和横向 work timeline 不发生重叠或文字裁切。
- 验证浮动顶部菜单在 profile 页面中仍然可用。

## Open Implementation Notes / 实现备注

Generate profile hashes in application code using `RandomNumberGenerator`, then encode with base64url or hex. Prefer at least 128 bits of entropy; 192 bits is a comfortable default.

在应用代码中使用 `RandomNumberGenerator` 生成 profile hash，然后用 base64url 或 hex 编码。熵至少应为 128 bit；192 bit 是更稳妥的默认值。

Hash rotation should be centralized in a candidate profile service so future resume-edit endpoints cannot forget to invalidate old public links.

Hash 轮换逻辑应集中放在 candidate profile service 中，避免未来新增 resume-edit endpoint 时忘记让旧公开链接失效。

The implementation should avoid storing historical public hashes unless audit requirements appear later. For the first version, only the current `ProfileHash` is valid.

除非未来出现审计需求，第一版不应存储历史公开 hash。第一版中只有当前 `ProfileHash` 有效。
