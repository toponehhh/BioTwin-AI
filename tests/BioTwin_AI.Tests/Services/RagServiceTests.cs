using BioTwin_AI.Data;
using BioTwin_AI.Models;
using BioTwin_AI.Services;
using BioTwin_AI.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Globalization;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class RagServiceTests
    {
        [Fact]
        public async Task SearchAsync_CandidateCanOnlySearchOwnResumes()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1", UserRole.Candidate);

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            dbContext.ResumeEntries.AddRange(
                CreateResumeEntry("candidate1", "Candidate1 Resume", "Experience in C#"),
                CreateResumeEntry("candidate2", "Candidate2 Resume", "Experience in Python"));
            await dbContext.SaveChangesAsync();

            var results = await ragService.SearchAsync("experience", limit: 10);

            Assert.Single(results);
            Assert.Contains("Experience in C#", results[0].Content);
        }

        [Fact]
        public async Task SearchAsync_InterviewerCanSearchAllResumes()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.InterviewerLogin();

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            dbContext.ResumeEntries.AddRange(
                CreateResumeEntry("candidate1", "Candidate1 Resume", "Experience in C#"),
                CreateResumeEntry("candidate2", "Candidate2 Resume", "Experience in Python"));
            await dbContext.SaveChangesAsync();

            var results = await ragService.SearchAsync("experience", limit: 10);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Contains("[candidate", r.Content));
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmptyListWhenNoMatches()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            var results = await ragService.SearchAsync("nonexistent", limit: 5);

            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchAsync_RespectLimitParameter()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            for (int i = 0; i < 10; i++)
            {
                dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", $"Resume {i}", $"Content {i}"));
            }
            await dbContext.SaveChangesAsync();

            var results = await ragService.SearchAsync("content", limit: 3);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public async Task SearchForChatAsync_UsesRerankOrderWhenAvailable()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = CreateConfig(enableRerank: true, rerankCandidateLimit: 2);
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync("csharp", 768))
                .ReturnsAsync(CreateVector(1f, 0f));

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[1,0]"), config);

            dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", "Strong Match", "C# direct", CreateVectorPayload(1f, 0f)));
            dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", "Weak Match", "LLM reranked", CreateVectorPayload(0.2f, 0.9f)));
            await dbContext.SaveChangesAsync();

            var results = await ragService.SearchForChatAsync("csharp", limit: 2);

            Assert.Equal(2, results.Count);
            Assert.Contains("Weak Match", results[0].Content);
            Assert.Contains("Strong Match", results[1].Content);
        }

        [Fact]
        public async Task SearchForChatAsync_FallsBackToVectorRankingWhenRerankResponseInvalid()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = CreateConfig(enableRerank: true, rerankCandidateLimit: 2);
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync("csharp", 768))
                .ReturnsAsync(CreateVector(1f, 0f));

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("not-json"), config);

            dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", "Strong Match", "C# direct", CreateVectorPayload(1f, 0f)));
            dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", "Weak Match", "LLM reranked", CreateVectorPayload(0.2f, 0.9f)));
            await dbContext.SaveChangesAsync();

            var results = await ragService.SearchForChatAsync("csharp", limit: 2);

            Assert.Equal(2, results.Count);
            Assert.Contains("Strong Match", results[0].Content);
            Assert.Contains("Weak Match", results[1].Content);
        }

        [Fact]
        public async Task CreateEmbeddingPayloadAsync_CallsEmbeddingService()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();
            var testEmbedding = Enumerable.Range(0, 768).Select(i => (float)i / 768f).ToArray();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(testEmbedding);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            var payload = await ragService.CreateEmbeddingPayloadAsync(
                "test content",
                new Dictionary<string, string>
                {
                    { "title", "Skills" },
                    { "parent_section_title", "Experience" }
                });

            Assert.NotEmpty(payload);
            Assert.StartsWith("[", payload);
            Assert.EndsWith("]", payload);
            embeddingServiceMock.Verify(
                x => x.GetEmbeddingAsync(
                    "Section Title: Skills\n\nParent Section Title: Experience\n\nSection Content:\ntest content",
                    768),
                Times.Once);
        }

        [Fact]
        public async Task InitializeAsync_LogsRagInitialization()
        {
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();

            var config = CreateConfig();
            var embeddingServiceMock = new Mock<IEmbeddingService>();

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = CreateRagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, new FakeChatClient("[0]"), config);

            await ragService.InitializeAsync();

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() != null && v.ToString()!.Contains("RAG initialized")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        private static RagService CreateRagService(
            BioTwinDbContext dbContext,
            ILogger<RagService> logger,
            CurrentUserSession session,
            IEmbeddingService embeddingService,
            IChatClient chatClient,
            IConfiguration config)
        {
            return new RagService(dbContext, logger, session, embeddingService, chatClient, config);
        }

        private static IConfiguration CreateConfig(bool enableRerank = false, int rerankCandidateLimit = 8)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Rag:EmbeddingSize", "768" },
                    { "Rag:EnableRerank", enableRerank.ToString() },
                    { "Rag:RerankCandidateLimit", rerankCandidateLimit.ToString(CultureInfo.InvariantCulture) },
                    { "LLM:Provider", "Ollama" },
                    { "LLM:Model", "qwen2.5:7b" },
                    { "LLM:ChatNumCtx", "2048" }
                })
                .Build();
        }

        private static ResumeEntry CreateResumeEntry(string tenantId, string title, string content, string? embeddingPayload = null)
        {
            return new ResumeEntry
            {
                TenantId = tenantId,
                SourceFileName = $"{tenantId}.pdf",
                Sections =
                {
                    new ResumeSection
                    {
                        TenantId = tenantId,
                        Title = title,
                        Content = content,
                        Vector = new ResumeSectionVector
                        {
                            TenantId = tenantId,
                            SectionTitle = title,
                            Content = content,
                            EmbeddingPayload = embeddingPayload ?? "[" + string.Join(",", Enumerable.Repeat(1f, 768)) + "]"
                        }
                    }
                }
            };
        }

        private static float[] CreateVector(float first, float second)
        {
            var vector = new float[768];
            vector[0] = first;
            vector[1] = second;
            return vector;
        }

        private static string CreateVectorPayload(float first, float second)
        {
            return "[" + string.Join(",", CreateVector(first, second).Select(value => value.ToString("G9", CultureInfo.InvariantCulture))) + "]";
        }

        private sealed class FakeChatClient : IChatClient
        {
            private readonly string _responseText;

            public FakeChatClient(string responseText)
            {
                _responseText = responseText;
            }

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText)));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, _responseText);
            }

            public object? GetService(Type serviceType, object? serviceKey = null)
            {
                return null;
            }

            public void Dispose()
            {
            }
        }
    }
}
