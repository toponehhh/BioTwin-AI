using BioTwin_AI.Data;
using BioTwin_AI.Models;
using BioTwin_AI.Services;
using BioTwin_AI.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class RagServiceTests
    {
        [Fact]
        public async Task SearchAsync_CandidateCanOnlySearchOwnResumes()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1", UserRole.Candidate);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]); // Return dummy embedding

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Add test data
            dbContext.ResumeEntries.AddRange(
                CreateResumeEntry("candidate1", "Candidate1 Resume", "Experience in C#"),
                CreateResumeEntry("candidate2", "Candidate2 Resume", "Experience in Python")
            );
            await dbContext.SaveChangesAsync();

            // Act
            var results = await ragService.SearchAsync("experience", limit: 10);

            // Assert
            Assert.Single(results); // Should only get candidate1's resume
            Assert.Contains("Experience in C#", results[0].Content);
        }

        [Fact]
        public async Task SearchAsync_InterviewerCanSearchAllResumes()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.InterviewerLogin(); // Login as interviewer

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Add test data
            dbContext.ResumeEntries.AddRange(
                CreateResumeEntry("candidate1", "Candidate1 Resume", "Experience in C#"),
                CreateResumeEntry("candidate2", "Candidate2 Resume", "Experience in Python")
            );
            await dbContext.SaveChangesAsync();

            // Act
            var results = await ragService.SearchAsync("experience", limit: 10);

            // Assert
            Assert.Equal(2, results.Count); // Interviewer should get both resumes
            Assert.All(results, r => Assert.Contains("[candidate", r.Content)); // Should include candidate ID
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmptyListWhenNoMatches()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Act - database is empty
            var results = await ragService.SearchAsync("nonexistent", limit: 5);

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchAsync_RespectLimitParameter()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("candidate1");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new float[768]);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Add test data - 10 resumes for candidate1
            for (int i = 0; i < 10; i++)
            {
                dbContext.ResumeEntries.Add(CreateResumeEntry("candidate1", $"Resume {i}", $"Content {i}"));
            }
            await dbContext.SaveChangesAsync();

            // Act
            var results = await ragService.SearchAsync("content", limit: 3);

            // Assert
            Assert.Equal(3, results.Count); // Should respect limit
        }

        [Fact]
        public async Task CreateEmbeddingPayloadAsync_CallsEmbeddingService()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();
            var testEmbedding = Enumerable.Range(0, 768).Select(i => (float)i / 768f).ToArray();
            embeddingServiceMock
                .Setup(x => x.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(testEmbedding);

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Act
            var payload = await ragService.CreateEmbeddingPayloadAsync("test content", new Dictionary<string, string>());

            // Assert
            Assert.NotEmpty(payload);
            Assert.StartsWith("[", payload);
            Assert.EndsWith("]", payload);
            embeddingServiceMock.Verify(x => x.GetEmbeddingAsync("test content", 768), Times.Once);
        }

        [Fact]
        public async Task InitializeAsync_LogsRagInitialization()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Rag:EmbeddingSize", "768" } })
                .Build();

            var embeddingServiceMock = new Mock<IEmbeddingService>();

            var loggerMock = new Mock<ILogger<RagService>>();
            var ragService = new RagService(dbContext, loggerMock.Object, session, embeddingServiceMock.Object, config);

            // Act
            await ragService.InitializeAsync();

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() != null && v.ToString()!.Contains("RAG initialized")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        private static ResumeEntry CreateResumeEntry(string tenantId, string title, string content)
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
                        EmbeddingPayload = "[" + string.Join(",", Enumerable.Repeat(1f, 768)) + "]"
                    }
                }
            };
        }
    }
}
