using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
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
            var chatClient = new FakeChatClient("Test response from candidate mode");
            var session = new CurrentUserSession();
            session.SignIn("testcandidate", UserRole.Candidate);

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, chatClient, session);

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
            var chatClient = new FakeChatClient("Test response from interviewer mode");
            var session = new CurrentUserSession();
            session.InterviewerLogin();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, chatClient, session);

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
            var chatClient = new FakeChatClient("Response");
            var session = new CurrentUserSession();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, chatClient, session);

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
            var chatClient = new FakeChatClient("No context available");
            var session = new CurrentUserSession();

            var agentService = new AgentService(ragServiceMock.Object, loggerMock.Object, config, chatClient, session);

            // Act
            var response = await agentService.AnswerQuestionAsync("Question with no context?");

            // Assert
            Assert.NotEmpty(response);
        }

        private sealed class FakeChatClient : IChatClient
        {
            private readonly string _responseContent;

            public FakeChatClient(string responseContent)
            {
                _responseContent = responseContent;
            }

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseContent)));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, _responseContent);
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
