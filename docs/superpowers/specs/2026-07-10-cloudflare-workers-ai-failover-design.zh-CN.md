# Cloudflare Workers AI 主服务与多级回退设计

日期：2026-07-10
状态：已确认

## 1. 目标与范围

`BioTwin_AI.AspNetCoreApi` 将 Cloudflare Workers AI 作为大语言模型、Embedding 和 Rerank 的默认服务，并保留本地模型或 OpenRouter 作为自动回退。Blazor 客户端仍然只调用 BioTwin API，不直接接触第三方端点、模型名称或任何提供方密钥。

本次改造仅覆盖 `src/BioTwin_AI.AspNetCoreApi`。当前分支不修改旧后端 `src/BioTwin_AI`，也不复用其中的服务实现。

业务范围包括：

- 聊天、简历润色、简历合并和简历结构化抽取；
- 简历 Section 向量生成与查询向量生成；
- RAG 粗召回结果的语义重排。

## 2. 提供方与模型

Cloudflare 大语言模型和 Embedding 使用 OpenAI 兼容接口：

`https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1`

Rerank 使用 Workers AI 原生 REST 接口：

`https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/{model}`

默认模型和回退顺序如下：

| 能力 | Cloudflare 主模型 | 回退 |
| --- | --- | --- |
| 普通聊天、润色和合并 | `@cf/meta/llama-3.1-8b-instruct-fast` | OpenRouter |
| 简历结构化抽取 | `@cf/meta/llama-3.3-70b-instruct-fp8-fast` | OpenRouter |
| Embedding | `@cf/baai/bge-m3` | 本地 BGE-M3 ONNX |
| Rerank | `@cf/baai/bge-reranker-base` | 本地 BGE reranker ONNX |

OpenRouter 保留现有地址、密钥和模型配置，但不再是默认路径。管理员可以关闭 LLM 自动回退或显式调整首选提供方。Embedding 和 Rerank 的 Cloudflare 主路径失败时不调用 OpenRouter。

## 3. 配置结构

非敏感配置存放在 `appsettings.json`：

```json
{
  "LLM": {
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "OpenRouter",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 180,
    "ExtractionTimeoutSeconds": 600
  },
  "CloudflareAI": {
    "AccountId": "",
    "ApiToken": "",
    "ChatModel": "@cf/meta/llama-3.1-8b-instruct-fast",
    "ExtractionModel": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
    "EmbeddingModel": "@cf/baai/bge-m3",
    "RerankModel": "@cf/baai/bge-reranker-base"
  },
  "OpenRouter": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "ApiKey": "",
    "ChatModel": "openrouter/free",
    "ExtractionModel": "openai/gpt-oss-20b:free"
  },
  "Embedding": {
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "BgeM3Onnx",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 60,
    "ExpectedDimensions": 1024,
    "ModelDirectory": "../../../LLM/bge_m3"
  },
  "Rerank": {
    "Enabled": true,
    "PrimaryProvider": "CloudflareWorkersAI",
    "FallbackProvider": "BgeRerankerOnnx",
    "FallbackEnabled": true,
    "FallbackCooldownSeconds": 60,
    "RequestTimeoutSeconds": 60,
    "CandidateCount": 20,
    "ModelDirectory": "../../../LLM/bge_rerank_v2"
  }
}
```

`CloudflareAI:ApiToken`、`OpenRouter:ApiKey` 和生产环境 Account ID 必须由 User Secrets、环境变量或部署平台密钥注入。它们不得进入客户端发布文件或日志。

## 4. API 内部组件

### 4.1 大语言模型

API 自行实现 `CloudflareChatClient : IChatClient`，直接调用 Cloudflare OpenAI 兼容 HTTP 端点。该客户端统一负责普通非流式响应、JSON Schema 对象型 `message.content`、SSE 流式事件、完成原因和基础用量元数据的转换。这样可以兼容 Cloudflare 在 JSON Mode 下直接返回 JSON 对象的行为，避免通用 OpenAI .NET 适配器把对象内容丢弃为空文本。

OpenRouter 继续使用现有 OpenAI SDK 客户端。`ILlmChatService` 仍作为业务层边界，负责模型选择、结果校验、自动回退和安全日志；业务服务不区分两个提供方的传输实现。

配置绑定和客户端创建从 `Program.cs` 的零散静态方法中提取到专用注册扩展，便于单元测试和以后增加其他提供方。

### 4.2 Embedding

`IEmbeddingService` 继续作为简历保存、向量重建和查询的统一边界。新增组合式实现，先调用 Cloudflare BGE-M3；发生可回退故障时调用现有本地 BGE-M3 ONNX 实现。

每次 Cloudflare 返回后必须验证：

- 响应包含且只包含所请求文本对应的向量；
- 每个向量维度等于 `ExpectedDimensions`；
- 所有数值均为有限值；
- 向量不是全零向量，并在需要时执行与本地实现一致的归一化。

Cloudflare 和本地模型都失败时，保存或重建操作明确失败，不使用 Hashing embedding 写入生产向量。这样可以避免把不同语义空间的数据混入现有索引。

### 4.3 Rerank

新 API 新增独立的 `IRerankService`，不引用旧后端实现。`RagSearchService` 首先按向量相似度和现有词法加权取 `CandidateCount` 条候选，再调用 Cloudflare reranker，最后截取客户端请求的数量。

Cloudflare rerank 失败时调用本地 ONNX reranker。若两种 reranker 都失败，记录警告并保留原始粗召回顺序，使检索仍可返回降级结果。Rerank 分数仅用于当前请求，不写入数据库，因此两个 reranker 使用不同模型不会污染持久化向量。

## 5. 自动回退与冷却

以下情况触发 Cloudflare 到对应回退提供方的自动切换：

- Cloudflare 未配置，但回退提供方已正确配置；
- 网络连接失败或提供方超时；
- HTTP `408`、`429` 或 `5xx`；
- 返回空正文、空向量或空排序结果；
- JSON、Embedding 或 Rerank 响应结构无效；
- 向量维度、数值或数量校验失败；
- Cloudflare 明确返回当前模型或 JSON Mode 无法满足请求。

以下情况不得回退：

- 用户主动取消请求；
- BioTwin 自身的输入验证失败；
- 主服务和回退服务都未配置；
- LLM 已经开始向客户端输出流式正文后发生错误。

LLM、Embedding 和 Rerank 使用彼此独立的内存健康状态与 60 秒默认冷却期。某个模型端点失败不会让其他能力一起切换。冷却状态不修改数据库，重启 API 后自动清空。

## 6. JSON 与简历抽取

简历抽取继续发送 JSON Schema，并使用 Cloudflare 官方支持 JSON Mode 的模型。业务层继续声明低 reasoning 预算并提供足够的正文输出额度。当前 Cloudflare 主模型 `llama-3.3-70b-instruct-fp8-fast` 的输入协议不支持 reasoning 参数，因此自有 Cloudflare 客户端不发送该字段；支持 reasoning 的 OpenRouter 回退模型仍通过其 SDK 客户端接收该设置。

`LlmChatService` 在 JSON 请求返回后先执行语法级 JSON 校验。Cloudflare 返回空内容、普通说明文字或截断 JSON 时，在业务层反序列化之前切换到 OpenRouter。`ResumeWizardExtractionService` 仍执行 DTO 反序列化、字段规范化和最多两次业务尝试。

如果两个提供方都失败，导入任务返回现有的可操作错误信息，不写入半成品简历，也不覆盖已有简历。

## 7. 向量迁移与一致性

Cloudflare 与本地路径都使用 BGE-M3，并强制输出相同的 1024 维向量。实现和测试仍需检查维度、归一化及同一组样本文本的相似度排序，不能仅凭模型名称假设兼容。

部署后不在 API 启动时自动重建已有向量，也不增加数据库迁移。管理员通过现有向量重建 API 显式触发一次全量重建，使已有简历统一使用当前 Cloudflare 主路径生成向量。界面或 API 响应应明确说明这是数据修改操作。

重建失败时保留已成功写入前的既有行为与错误边界，不在启动流程中静默修改业务数据。后续如需记录模型版本或增量迁移，再单独设计持久化版本信息。

## 8. 流式调用

`CloudflareChatClient` 直接解析 OpenAI 兼容端点返回的 SSE `data:` 事件，忽略空行和 `[DONE]`，并把 `choices[].delta.content` 转换为 `ChatResponseUpdate`。流式聊天只允许在尚未输出任何正文时回退。如果 Cloudflare 在第一个正文片段之前失败，API 改用 OpenRouter 重新发起请求。如果已经输出正文，则记录提供方错误并结束该流，避免回退后产生重复或互相矛盾的内容。JSON Mode 不使用流式调用。

## 9. 日志与安全

提供方日志只包含：能力类型、提供方名称、模型、耗时、HTTP 状态、finish reason、token 数、向量数量和维度、候选数量以及是否发生回退。

日志不得包含：简历 Markdown、Embedding 原文、Rerank query 或 context、提示词、模型正文、Authorization header、API Token、Account ID、Cookie、用户 Profile Hash 或 URL query string。

客户端远程日志继续通过 `POST /api/client-logs` 进入 BioTwin API，与模型提供方日志保持独立。

## 10. 测试与验收

单元测试覆盖：

- Cloudflare LLM 正常返回时不调用 OpenRouter；
- Cloudflare 空响应、无效 JSON、超时、`429` 和 `5xx` 时调用 OpenRouter；
- Cloudflare embedding 正常时不加载本地推理结果；
- Cloudflare embedding 超时、无效维度、非有限值或空结果时回退本地 ONNX；
- Cloudflare 与本地 embedding 都失败时不写入 Hashing 向量；
- RAG 先粗召回候选，再按 Cloudflare rerank 结果返回；
- Cloudflare rerank 失败时使用本地 reranker；
- 两种 reranker 都失败时保持粗召回顺序；
- 用户取消不触发任何能力的回退；
- 三种能力的冷却状态互不影响，冷却结束后恢复探测；
- 日志不包含提示词、简历正文、Embedding 原文和密钥；
- 配置绑定生成正确的 Cloudflare Endpoint、模型名称和超时。

集成验证使用完全虚构的长简历和检索样本，不向诊断请求发送真实候选人信息。验证 Cloudflare 与本地 BGE-M3 返回相同维度，并对样本产生一致的相似度排序。完成后运行 API、客户端和解决方案构建测试，并关闭 BioTwin、All2MD 和 .NET 构建服务。

## 11. 非目标

- 本次不修改 `src/BioTwin_AI` 旧后端；
- 不把 ASP.NET Core API 迁移到 Cloudflare Workers 或 Containers；
- 不迁移 SQLite、R2 或 Vectorize；
- 不删除本地 ONNX 模型或下载脚本；
- 不让 Blazor 客户端直接调用 Cloudflare；
- 不在本次实现持久化提供方健康状态、模型版本或引入 Redis；
- 不在 API 启动时自动重建已有向量。
