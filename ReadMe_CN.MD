# BioTwin_AI - AI-Powered Digital Twin Interview Assistant

一个基于 Blazor Server + RAG + 向量数据库的智能面试助手系统，让面试官与求职者进行互动对话，系统基于简历信息进行回答。

## 🏗️ 架构概述

### 技术栈
- **前端**: Blazor Server (Interactive Server Components)
- **后端**: ASP.NET Core 10.0
- **数据存储**: 
  - SQLite (简历文档存储)
  - Qdrant (向量数据库)
- **文件转换**: All2MD API (将上传文件转换为 Markdown)
- **AI Agent Framework**: Microsoft Agent Framework (集成了 LLM 交互)

### 系统流程
1. **上传简历** → 文件转换为 Markdown（调用 All2MD 服务）
2. **存储数据** → 内容保存到 SQLite
3. **向量化** → 将简历内容存入 Qdrant
4. **检索增强** → 面试官提问时，从向量数据库检索相关内容
5. **生成回答** → Agent 结合 RAG 上下文生成面试回答

## 🚀 快速开始

### 前置条件
- .NET 10.0 SDK
- Docker & Docker Compose (用于 Qdrant)
- All2MD 服务 (运行在 http://localhost:8000)

### 1. 启动 Qdrant 向量数据库

```bash
cd C:\Source\Repos\BioTwin_AI
docker-compose up -d
```

验证 Qdrant 是否正常运行：
```bash
curl http://localhost:6333/health
```

### 2. 启动 All2MD 文件转换服务（如果未运行）

```bash
# 在另一个终端
cd C:\Source\Repos\All2MD
uv run uvicorn all2md.server:app --port 8000
```

### 3. 构建并运行 BioTwin_AI

```bash
cd C:\Source\Repos\BioTwin_AI

# 构建
dotnet build

# 运行
dotnet run
```

应用将在 `http://localhost:5000` 启动

## 📱 功能使用

### 主界面
- **左侧边栏**: 显示已上传的简历各个板块
- **右侧聊天区**: 面试官提问和 AI 回答区域
- **顶部按钮**: 上传新的简历部分

### 上传简历
1. 点击 "+ Add Section" 按钮
2. 输入板块标题（如 "Education", "Experience", "Skills"）
3. 选择文件（PDF, DOCX, PPTX, HTML, TXT）
4. 点击 "Upload & Convert"
5. 系统自动转换为 Markdown 并索引

### 聊天对话
1. 在输入框输入问题
2. 按 Enter 或点击 Send
3. 系统从 RAG 检索相关简历内容
4. Agent 生成基于简历的回答

## 🏗️ 项目结构

```
BioTwin_AI/
├── Components/          # Blazor 组件
│   ├── Pages/
│   │   ├── Index.razor             # 主聊天页面
│   │   └── ResumeUpload.razor      # 上传页面
│   ├── ResumeSidebarComponent.razor # 左侧简历列表
│   └── ChatInterfaceComponent.razor # 聊天界面
├── Data/
│   └── BioTwinDbContext.cs         # EF Core DbContext
├── Models/
│   └── ResumeEntry.cs              # 简历条目模型
├── Services/
│   ├── RagService.cs               # 向量检索服务
│   ├── AgentService.cs             # AI Agent 服务
│   └── ResumeUploadService.cs      # 文件上传和转换服务
├── Program.cs                       # 依赖注入配置
├── appsettings.json                # 配置文件
└── docker-compose.yml              # Qdrant 容器配置
```

## 🔧 配置文件 (appsettings.json)

```json
{
  "Qdrant": {
    "Url": "http://localhost:6333"  // Qdrant 服务地址
  },
  "All2MD": {
    "ApiUrl": "http://localhost:8000"  // All2MD 服务地址
  }
}
```

## 🧠 RAG 工作流程

1. **检索** (Retrieval)
   - 用户提问时，使用查询文本生成 embedding
   - 在 Qdrant 向量数据库中进行相似度搜索
   - 返回 Top-5 最相关的简历片段

2. **增强** (Augmentation)
   - 将检索到的相关简历内容作为 Context
   - 组织成格式化的提示词

3. **生成** (Generation)
   - Agent 结合 Context 和查询生成回答
   - 当前使用原型响应生成器，可扩展为真实 LLM API

## 🔌 集成点

### All2MD 服务
- **端点**: POST `/convert/json`
- **输入**: 多部分表单数据（文件）
- **输出**: JSON 格式 Markdown 内容

### Qdrant 向量数据库
- **地址**: http://localhost:6333
- **集合**: resume_embeddings
- **向量大小**: 384 维

### Agent Service
- 当前是原型实现，支持简单的关键词匹配
- 可升级为真实 LLM API（Azure OpenAI、Claude 等）

## 📊 数据库架构

### SQLite (biotwin.db)
```sql
ResumeEntries {
  Id: int (PK),
  Title: string,           -- 简历部分标题
  Content: string,         -- Markdown 内容
  SourceFileName: string,  -- 原始文件名
  CreatedAt: datetime,
  VectorId: string         -- Qdrant 向量 ID
}
```

## 🚨 故障排查

### Qdrant 连接失败
```bash
# 检查 Qdrant 状态
docker ps | grep qdrant
docker logs qdrant_biotwin

# 重启 Qdrant
docker-compose restart
```

### All2MD 连接失败
```bash
# 确保 All2MD 服务正在运行
curl http://localhost:8000/health
```

### 数据库迁移错误
```bash
# 删除旧数据库并重新创建
rm biotwin.db
dotnet run
```

## 🔮 后续改进

- [ ] 集成真实 LLM API（Azure OpenAI, Claude）
- [ ] 改进向量化模型（使用真实的 embedding 模型）
- [ ] 添加多语言支持
- [ ] 实现流式输出（Streaming）
- [ ] 添加音频输入（语音面试）
- [ ] 支持多用户会话
- [ ] 添加面试报告生成
- [ ] 性能优化和缓存

## 📝 许可证

MIT License

## 👨‍💻 开发

所有组件使用 Blazor Server 渲染模式，C# 编写。

```bash
# 开发模式运行
dotnet run --configuration Debug

# 构建生产版本
dotnet build --configuration Release
dotnet publish --configuration Release
```

## 📞 支持

有问题？检查日志文件或查看源代码文档。
