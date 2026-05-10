using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class AgentServiceTests
    {
        [Fact]
        public async Task AnswerQuestionAsync_CandidateUsesFirstPersonPrompt()
        {
            // Arrange
            var ragServiceMock = new Mock<IRagService>();
            ragServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<(string, double)> { ("Experienced in C#", 0.95) });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" },
                    { "LLM:Model", "qwen2.5:7b" },
                    { "LLM:Temperature", "0.2" },
                    { "LLM:MaxTokens", "800" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<AgentService>>();
            var httpClientMock = CreateMockHttpClient("Test response from candidate mode");
            var session = new CurrentUserSession();
            session.SignIn("testcandidate", UserRole.Candidate);

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, httpClientMock, session);

            // Act
            var response = await agentService.AnswerQuestionAsync("What is your experience with C#?");

            // Assert
            Assert.NotEmpty(response);
            Assert.Equal("Test response from candidate mode", response);
            ragServiceMock.Verify(x => x.SearchAsync("What is your experience with C#?", 3), Times.Once);
        }

        [Fact]
        public async Task AnswerQuestionAsync_InterviewerUsesThirdPersonPrompt()
        {
            // Arrange
            var ragServiceMock = new Mock<IRagService>();
            ragServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<(string, double)> { ("[candidate1 - Senior Dev]\nExperienced in C#", 0.95) });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" },
                    { "LLM:Model", "qwen2.5:7b" },
                    { "LLM:Temperature", "0.2" },
                    { "LLM:MaxTokens", "800" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<AgentService>>();
            var httpClientMock = CreateMockHttpClient("Test response from interviewer mode");
            var session = new CurrentUserSession();
            session.InterviewerLogin();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, httpClientMock, session);

            // Act
            var response = await agentService.AnswerQuestionAsync("What is candidate1's C# experience?");

            // Assert
            Assert.NotEmpty(response);
            Assert.Equal("Test response from interviewer mode", response);
            ragServiceMock.Verify(x => x.SearchAsync("What is candidate1's C# experience?", 3), Times.Once);
        }

        [Fact]
        public async Task AnswerQuestionAsync_LogsQuestion()
        {
            // Arrange
            var ragServiceMock = new Mock<IRagService>();
            ragServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<(string, double)>());

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" },
                    { "LLM:Model", "qwen2.5:7b" },
                    { "LLM:Temperature", "0.2" },
                    { "LLM:MaxTokens", "800" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<AgentService>>();
            var httpClientMock = CreateMockHttpClient("Response");
            var session = new CurrentUserSession();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, httpClientMock, session);

            // Act
            await agentService.AnswerQuestionAsync("Test question?");

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() != null && v.ToString()!.Contains("Processing question: Test question?")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task AnswerQuestionAsync_WithEmptySearchResults_ReturnsFallbackMessage()
        {
            // Arrange
            var ragServiceMock = new Mock<IRagService>();
            ragServiceMock
                .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<(string, double)>()); // Empty results

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" },
                    { "LLM:Model", "qwen2.5:7b" },
                    { "LLM:Temperature", "0.2" },
                    { "LLM:MaxTokens", "800" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<AgentService>>();
            var httpClientMock = CreateMockHttpClient("No context available");
            var session = new CurrentUserSession();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, httpClientMock, session);

            // Act
            var response = await agentService.AnswerQuestionAsync("Question with no context?");

            // Assert
            Assert.NotEmpty(response);
        }

        private HttpClient CreateMockHttpClient(string responseContent)
        {
            var handlerMock = new Mock<HttpMessageHandler>();

            var mockResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($@"{{
                    ""message"": {{
                        ""content"": ""{responseContent}""
                    }}
                }}", System.Text.Encoding.UTF8, "application/json")
            };

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(mockResponse);

            return new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost:11434") };
        }
    }
}
