# BioTwin_AI - AI-Powered Digital Twin Interview Assistant

An intelligent interview assistant built with Blazor Server + RAG + vector database technologies. It enables interactive conversations between interviewers and candidates, with answers grounded in resume content.

## 🏗️ Architecture Overview

### Tech Stack
- **Frontend**: Blazor Server (Interactive Server Components)
- **Backend**: ASP.NET Core 10.0
- **Data Storage**:
  - SQLite (resume document storage)
  - Qdrant (vector database)
- **File Conversion**: All2MD API (converts uploaded files into Markdown)
- **AI Agent Framework**: Microsoft Agent Framework (integrated LLM interaction)

### System Flow
1. **Upload Resume** -> Convert files to Markdown (via All2MD service)
2. **Store Data** -> Save content into SQLite
3. **Vectorize** -> Store resume embeddings in Qdrant
4. **Retrieval Augmentation** -> Retrieve relevant content from the vector database when a question is asked
5. **Answer Generation** -> Agent generates interview responses using RAG context

## 🚀 Quick Start

### Prerequisites
- .NET 10.0 SDK
- Docker & Docker Compose (for Qdrant)
- All2MD service (running at http://localhost:8000)

### 1. Start the Qdrant Vector Database

```bash
cd C:\Source\Repos\BioTwin_AI
docker-compose up -d
```

Verify Qdrant is running:
```bash
curl http://localhost:6333/health
```

### 2. Start the All2MD File Conversion Service (if not running)

```bash
# In another terminal
cd C:\Source\Repos\All2MD
uv run uvicorn all2md.server:app --port 8000
```

### 3. Build and Run BioTwin_AI

```bash
cd C:\Source\Repos\BioTwin_AI

# Build
dotnet build

# Run
dotnet run
```

The app will start at `http://localhost:5000`.

## 📱 Features and Usage

### Main Interface
- **Left Sidebar**: Displays uploaded resume sections
- **Right Chat Area**: Interviewer questions and AI responses
- **Top Button**: Upload a new resume section

### Upload Resume
1. Click the "+ Add Section" button
2. Enter a section title (for example, "Education", "Experience", "Skills")
3. Select a file (PDF, DOCX, PPTX, HTML, TXT)
4. Click "Upload & Convert"
5. The system automatically converts content to Markdown and indexes it

### Chat Conversation
1. Enter a question in the input box
2. Press Enter or click Send
3. The system retrieves relevant resume content via RAG
4. The Agent generates a resume-grounded response

## 🏗️ Project Structure

```
BioTwin_AI/
├── src/
│   └── BioTwin_AI/
│       ├── Components/             # Blazor components
│       ├── Data/                   # EF Core DbContext
│       ├── Models/                 # Domain models
│       ├── Services/               # RagService, AgentService, Upload service, etc.
│       ├── Program.cs              # Dependency injection and app startup
│       ├── appsettings.json        # Runtime configuration
│       └── appsettings.Development.json
├── tests/
│   └── BioTwin_AI.Tests/           # xUnit test project
├── database/                       # SQLite database files
├── docker-compose.yml              # Container services (for local dependencies)
└── BioTwin_AI.slnx                 # Solution file
```

## 🔧 Configuration File (appsettings.json)

```json
{
  "Qdrant": {
    "Url": "http://localhost:6333"  // Qdrant service URL
  },
  "All2MD": {
    "ApiUrl": "http://localhost:8000"  // All2MD service URL
  }
}
```

## 🧠 RAG Workflow

1. **Retrieval**
   - Generate embeddings from user questions
   - Perform similarity search in Qdrant
   - Return top-5 most relevant resume chunks

2. **Augmentation**
   - Use retrieved resume content as context
   - Build formatted prompts

3. **Generation**
   - Agent generates answers using context + query
   - Currently uses a prototype response generator, and can be extended to real LLM APIs

## 🔌 Integration Points

### All2MD Service
- **Endpoint**: POST `/convert/json`
- **Input**: Multipart form data (file)
- **Output**: Markdown content in JSON format

### Qdrant Vector Database
- **URL**: http://localhost:6333
- **Collection**: resume_embeddings
- **Vector Size**: 384 dimensions

### Agent Service
- Currently a prototype implementation with basic keyword matching
- Can be upgraded to real LLM APIs (Azure OpenAI, Claude, etc.)

## 📊 Database Schema

### SQLite (biotwin.db)
```sql
ResumeEntries {
  Id: int (PK),
  Title: string,           -- Resume section title
  Content: string,         -- Markdown content
  SourceFileName: string,  -- Original file name
  CreatedAt: datetime,
  VectorId: string         -- Qdrant vector ID
}
```

## 🚨 Troubleshooting

### Qdrant Connection Failure
```bash
# Check Qdrant status
docker ps | grep qdrant
docker logs qdrant_biotwin

# Restart Qdrant
docker-compose restart
```

### All2MD Connection Failure
```bash
# Ensure All2MD service is running
curl http://localhost:8000/health
```

### Database Migration Error
```bash
# Remove old database and recreate
rm biotwin.db
dotnet run
```

## 🔮 Future Improvements

- [ ] Integrate real LLM APIs (Azure OpenAI, Claude)
- [ ] Improve vectorization model (use real embedding models)
- [ ] Add multilingual support
- [ ] Implement streaming output
- [ ] Add audio input (voice interview)
- [ ] Support multi-user sessions
- [ ] Add interview report generation
- [ ] Performance optimization and caching

## 📝 License

MIT License

## 👨‍💻 Development

All components use Blazor Server rendering mode and are written in C#.

```bash
# Run in development mode
dotnet run --configuration Debug

# Build production artifacts
dotnet build --configuration Release
dotnet publish --configuration Release
```

## 📞 Support

Need help? Check the logs or review the source code documentation.
