# 客户端远程日志设计

**状态：** 已于 2026-07-10 选择方案 1，等待书面设计确认

## 目标

BioTwin Blazor 客户端需要把关键运行错误通过 BioTwin API 发送到服务端日志。该功能必须覆盖公共 API 调用失败和未处理的 Blazor 组件异常，同时不能因为日志上传失败影响客户端功能，也不能上传简历正文、认证信息或其他敏感请求内容。

## 现有基础

客户端已经注册 `RemoteClientLoggerProvider`，并通过 `POST /api/client-logs` 将 `ClientLogEntryRequest` 发送到 API。API 的 `ClientLogsController` 已经可以把请求写入 `ILogger` 和 Serilog。

本次工作增强该链路，而不是建立第二套日志系统。当前缺口是许多页面捕获异常后只显示消息，没有写入 `ILogger`；根组件也没有统一的异常边界。现有 Provider 还会上传所有 Information 日志，容易产生不必要的噪声。

## 日志范围

默认上传 `Warning`、`Error` 和 `Critical`。`Debug` 和 `Trace` 永不上传。普通 `Information` 不上传，只有客户端启动成功事件作为明确例外上传一次。

以下情况必须记录：

- BioTwin API 返回非成功状态码；
- BioTwin API 请求因网络故障意外失败；
- Blazor 组件发生未处理异常；
- 简历导入、保存等关键工作流捕获到非取消异常，且公共 API 层无法提供足够上下文。

用户主动取消请求产生的 `OperationCanceledException` 不作为错误上传。

## 客户端结构

`RemoteClientLoggerProvider` 继续作为所有 `ILogger` 的远程输出端。它执行级别过滤、递归保护、长度限制和异步发送。日志发送使用现有 BioTwin API `HttpClient`，但日志基础设施自身的 HTTP 分类必须被排除，防止上传失败递归产生日志。

`ApiClientBase` 成为 API 失败的公共记录点。它记录 HTTP 方法、去除 query string 的相对路径、状态码和异常类型，但不记录请求体、响应体或请求头。页面仍然收到现有的用户友好异常消息。

根路由外增加专用 `ClientErrorBoundary`。它通过 `ILogger<ClientErrorBoundary>` 记录未处理组件异常，并显示与现有站点一致的恢复界面。成功导航后可以恢复错误边界，避免一次组件错误永久破坏后续导航。

关键页面只在需要补充业务上下文时记录，例如导入任务 ID 和当前阶段。公共 API 层已经记录的同一异常不应被页面重复上传。

## 数据与隐私

每条日志允许包含：

- 日志级别；
- 分类名称；
- 不含用户文档内容的消息；
- 异常类型和经过长度限制的堆栈；
- 不含 query string 和 fragment 的页面路径；
- 客户端 UTC 时间。

不得上传请求体、响应体、Cookie、Authorization header、密码、简历 Markdown、上传文件内容、profile hash 或 URL query 参数。客户端和 API 都执行长度限制；分类和消息使用保守上限，异常堆栈允许更大但仍为有界值。API 使用结构化日志参数，防止客户端文本改变服务端日志模板。

## 服务端处理

`POST /api/client-logs` 保持为客户端唯一日志入口，并继续允许匿名启动阶段调用。API 校验级别并拒绝或忽略低于策略的日志，对所有字符串裁剪和限长，然后写入带有固定 `Client log` 标记的 Serilog 事件。

日志端点始终返回 `202 Accepted`。客户端发送失败时静默丢弃该条日志，不重试、不显示 UI 错误，也不触发另一条远程日志。

## 测试

自动化测试必须覆盖：

- Warning 及以上会发送，普通 Information、Debug 和 Trace 不发送；
- 启动 Information 例外会发送；
- 日志 HTTP 分类不会递归发送；
- URL query string、请求体和响应体不会进入日志；
- 消息和异常堆栈被限制长度；
- API 非成功响应会在公共客户端层记录一次；
- 用户取消不会记录为错误；
- 未处理组件异常由根错误边界记录；
- API 控制器只接受策略允许的级别并写入结构化日志。

## 非目标

本阶段不增加日志批量上传、离线持久队列、JavaScript `window.onerror`/`unhandledrejection` 桥接、浏览器性能追踪、用户行为分析或第三方遥测服务。这些能力可以在现有 `ILogger` 边界之后独立扩展。
