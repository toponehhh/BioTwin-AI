# 简历工作台导入与 Markdown 编辑器设计

## 概要

为已登录用户构建统一的简历工作台。该工作台用一个界面替代分散的上传和编辑流程，使用户可以导入文件、查看或编辑 Markdown、检查 Markdown 大纲，并保存某种语言下的唯一正式简历。

系统当前只支持两种简历语言：

- `zh-CN`：简体中文
- `en`：英语

每个用户在每种支持语言下最多只能有一份简历。继续导入同语言简历时，系统不会创建第二份简历，而是生成 AI 辅助合并预览，让用户检查合并后的 Markdown。只有用户确认保存后，系统才会更新该语言的正式简历。

## 目标

- 允许用户在一个工作台中选择并导入不同简历文件。
- 使用现有上传转换流程将导入文件转换为 Markdown。
- 用真正的 Markdown 编辑器替代普通 textarea。
- 显示当前 Markdown 标题结构的实时树状大纲。
- 允许用户在保存或合并前选择或修正检测出的简历语言。
- 强制每个用户每种语言只有一份正式简历。
- 保存简历时，将 Markdown 拆分为 section，重新生成向量，并刷新候选人展示数据。
- 导入同语言简历时，使用大模型按时间线合并，并要求用户先审阅再保存。

## 非目标

- 暂不增加更多语言。
- 暂不构建完整的块级简历编辑器。
- 不静默覆盖已有的同语言简历。
- 除非所选编辑器绝对需要，否则不引入新的前端构建流水线。
- 不允许启动代码修改数据库结构或插入 seed 数据。

## 用户体验

### 布局

工作台是高密度的 admin 工具，不是落地页。

- 左侧：`Library`
  - 显示两个语言槽位：简体中文和英语。
  - 在已有正式简历时显示对应简历。
  - 显示本次导入但尚未保存的草稿。
  - 包含文件导入控件。
- 中间：Markdown 编辑器
  - 编辑当前选中的草稿或正式简历 Markdown。
  - 包含标题和语言控件。
  - 根据当前状态显示主要操作。
- 右侧：大纲
  - 显示从 Markdown 标题解析出的实时嵌套树。
  - 使用标题级别展示层级关系。
  - 保存后可以用后端返回的 `ResumeSectionDto.Children` 刷新。

### 导入流程

1. 用户选择一个或多个文件。
2. 前端对每个文件调用现有上传转换 API。
3. 转换后的 Markdown 作为草稿出现在左侧。
4. 工作台根据 Markdown 内容检测语言。
5. 用户可以在保存或合并前手动修改语言。
6. 如果该语言还没有正式简历，主要操作为 `Save`。
7. 如果该语言已有正式简历，主要操作为 `Merge preview`。

### 新语言保存流程

1. 用户检查或编辑草稿 Markdown。
2. 用户点击 `Save`。
3. 后端使用所选语言创建 `ResumeEntry`。
4. 后端将 Markdown 拆分为 `ResumeSections`。
5. 后端在 `ResumeSectionVectors` 中重新生成向量。
6. 后端使用最终 Markdown 触发候选人展示数据抽取。
7. 工作台刷新左侧列表和右侧大纲。

### 同语言合并流程

1. 用户导入一份语言已存在正式简历的文件。
2. 用户点击 `Merge preview`。
3. 后端加载正式简历 Markdown，并与导入草稿 Markdown 一起作为输入。
4. 大模型合并两份 Markdown，重点保持时间线一致。
5. 后端返回合并后的 Markdown，但不保存。
6. 工作台将合并 Markdown 放入编辑器，作为可审阅草稿。
7. 用户按需编辑后点击 `Save`。
8. 后端更新该语言正式简历，重新拆分 section，重新生成向量，并刷新候选人展示数据。

## 语言检测

语言检测应保持轻量、确定性，可在前端或 shared code 中实现：

- 如果有意义文本中 CJK 字符占主导，选择 `zh-CN`。
- 如果拉丁字母占主导，选择 `en`。
- 如果无法明确判断，暂时默认 `zh-CN`，并允许用户修改。

草稿和可编辑简历始终显示语言选择器。

## 数据模型

在 `ResumeEntries` 中增加 `Language` 字段。

推荐取值：

- `zh-CN`
- `en`

增加唯一索引：

```sql
CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_Language
ON ResumeEntries(TenantId, Language);
```

现有数据迁移行为：

- 现有简历需要通过人工或一次性 SQL 脚本指定语言。
- 如果开发数据库当前为空，初始 schema 可以直接包含最终字段和索引。
- 运行时启动逻辑只能验证 schema，不能修改数据。

## 共享契约

为简历相关 DTO 和请求增加语言字段：

- `ResumeSummaryDto.Language`
- `ResumeDetailDto.Language`
- `ConvertedResumeFileDto.DetectedLanguage`
- `SaveResumeMarkdownRequest.Language`
- 新增 `MergeResumeMarkdownRequest`
- 新增 `MergeResumeMarkdownResponse`

合并预览请求应包含：

- language
- draft title
- draft Markdown
- 必要时包含源文件元数据用于 UI 上下文

响应应包含：

- language
- canonical resume id
- merged title
- merged Markdown
- 可选 warnings

## 后端服务

### ResumeService

扩展现有行为：

- 当 `(tenantId, language)` 不存在简历时，Save 创建正式简历。
- 当 `(tenantId, language)` 已存在简历时，Save 更新现有正式简历。
- Replace Markdown 继续负责拆分 section、重新生成向量，并触发候选人展示数据抽取。
- 按 source hash 做重复检测仍然有价值，但语言唯一性是独立规则。

### ResumeMergeService

新增一个专注于合并预览的服务：

- 输入：正式简历 Markdown、草稿 Markdown、语言、标题上下文。
- 输出：合并后的 Markdown。
- Prompt 应要求模型保留事实细节、按时间顺序合并、删除重复经历，并保留 Markdown 标题结构。
- 如果 LLM 合并失败，返回清晰错误，并保留两份文档供用户继续处理。

第一版实现可以复用现有已配置的 chat/model 基础设施。如果项目之后转向本地模型流程，不应引入只能依赖远端的路径。

## 前端组件

### ResumeWorkspace.razor

路由：

- 推荐路由：`/resume/workspace`
- 现有 `/resume/upload` 和 `/resume/edit/{ResumeId:int?}` 后续可以重定向到工作台，或嵌入工作台。

状态模型：

- 按语言分组的正式简历摘要
- 从导入文件生成的草稿项
- 当前选中项
- 当前标题
- 当前语言
- 当前 Markdown
- dirty 标记
- merge preview 状态
- status/error message

### Markdown 编辑器

第一版先使用静态 vendor 集成。

首选候选：EasyMDE。

原因：

- 专门面向 Markdown 编辑场景。
- 比 CodeMirror 6 接入成本更低。
- 第一版不需要 npm 或 Vite 流水线。

集成应封装在：

- `wwwroot/js/markdownEditor.js`
- 小型 Blazor 组件，例如 `MarkdownEditor.razor`

组件应支持：

- 设置初始 Markdown
- 变更通知
- Blazor 读写当前值
- 释放资源

### 大纲树

大纲树可以在前端从 Markdown 生成。

解析规则：

- 解析 ATX 标题行：`#`、`##`、`###` 等。
- 忽略 fenced code block 内的标题。
- 保留标题级别和文本。
- 按标题级别嵌套。
- 如果没有标题，显示空状态。

保存后，可以使用后端 section tree 校验或替换实时大纲。

## API

复用现有 API：

- `GET /api/resumes`
- `GET /api/resumes/{resumeId}`
- `POST /api/resumes/upload/convert`
- `POST /api/resumes`
- `PUT /api/resumes/{resumeId}/markdown`
- `GET /api/resumes/{resumeId}/export/markdown`

新增 API：

```text
POST /api/resumes/merge-preview
```

合并预览 API 不得写入数据库。

## 验证与错误处理

- 拒绝不支持的语言值。
- 空 Markdown 不能保存。
- 导入同语言简历时提示 merge preview，而不是创建重复简历。
- Merge preview 失败不能丢弃导入草稿。
- Save 失败时保留当前编辑器内容。
- 上传转换失败时显示后端错误或 placeholder Markdown，保持现有行为。

## 测试

后端测试：

- 保存第一份 `zh-CN` 简历会创建一个 entry 和 sections。
- 再保存一份 `zh-CN` 简历会更新已有正式 entry，而不是创建第二个 entry。
- 同一 tenant 下 `zh-CN` 和 `en` 可以共存。
- Merge preview 返回合并 Markdown，且不写入 `ResumeEntries`。
- Replace/save 仍然重新生成 sections 和 vectors。

共享契约测试：

- DTO 暴露语言字段。
- Merge preview request/response 可序列化。

Blazor 测试：

- Workspace 路由存在。
- Workspace 引用了 Markdown editor bridge。
- Workspace 包含语言选择器、library、editor 和 outline。
- 现有 upload/edit 路由仍可访问或明确重定向。

人工验证：

- 导入中文 Markdown 简历并保存。
- 导入第二份中文简历，生成 merge preview，编辑后保存，并确认只剩一份中文简历。
- 导入英文简历，并确认它创建独立正式简历。
- 确认保存后存在 sections 和 vectors。

## 未决问题

第一版实现没有未决问题。

已确认决策：

- 使用统一 Resume Workspace。
- 使用自动语言检测，并允许手动覆盖。
- 只支持 `zh-CN` 和 `en`。
- 覆盖同语言简历前必须先生成 merge preview。
- 优先采用静态 vendor Markdown 编辑器集成。
