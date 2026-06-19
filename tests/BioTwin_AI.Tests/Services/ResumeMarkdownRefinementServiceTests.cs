using BioTwin_AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class ResumeMarkdownRefinementServiceTests
    {
        private static Mock<IStringLocalizer<SharedResource>> CreateLocalizerMock()
        {
            var localizerMock = new Mock<IStringLocalizer<SharedResource>>();
            localizerMock
                .Setup(x => x[It.IsAny<string>()])
                .Returns((string key) => new LocalizedString(key, key));
            localizerMock
                .Setup(x => x[It.IsAny<string>(), It.IsAny<object[]>()])
                .Returns((string key, object[] args) => new LocalizedString(key, key));
            return localizerMock;
        }

        [Fact]
        public async Task RefineAsync_UsesConfiguredModelFromSettings()
        {
            // Arrange
            var fakeClient = new TestChatClient("# Title\n- Cleaned line");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ResumeMarkdownRefinement:Model", "openrouter/free" },
                    { "ResumeMarkdownRefinement:Temperature", "0.1" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                fakeClient,
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var result = await service.RefineAsync("## Bad - resume### text", "Test Resume");

            // Assert
            Assert.Contains("# Title", result);
            Assert.True(fakeClient.CapturedOptions is not null);
            Assert.Equal("openrouter/free", fakeClient.CapturedOptions!.ModelId);
        }

        [Fact]
        public async Task RefineAsync_ReturnsOriginalMarkdownWhenDisabled()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ResumeMarkdownRefinement:Enabled", "false" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                new TestChatClient("should not be called"),
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var result = await service.RefineAsync("## Original content", "Test Resume");

            // Assert
            Assert.Equal("## Original content", result);
        }

        [Fact]
        public async Task RefineAsync_ReturnsOriginalMarkdownWhenInputIsEmpty()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ResumeMarkdownRefinement:Enabled", "true" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                new TestChatClient("should not be called"),
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var result = await service.RefineAsync("", "Test Resume");

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public async Task RefineAsync_UsesFallbackModelWhenNotConfigured()
        {
            // Arrange
            var fakeClient = new TestChatClient("# Cleaned resume");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Model", "openrouter/free" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                fakeClient,
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var result = await service.RefineAsync("## Some content", "Test Resume");

            // Assert
            Assert.Contains("# Cleaned resume", result);
            Assert.True(fakeClient.CapturedOptions is not null);
            Assert.Equal("openrouter/free", fakeClient.CapturedOptions!.ModelId);
        }

        [Fact]
        public async Task RefineAsync_CleansModelMarkdownFences()
        {
            // Arrange
            var fakeClient = new TestChatClient("```markdown\n# Title\n- Item\n```\n");
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ResumeMarkdownRefinement:Model", "openrouter/free" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                fakeClient,
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var result = await service.RefineAsync("## Input", "Test Resume");

            // Assert
            Assert.DoesNotContain("```", result);
            Assert.Contains("# Title", result);
        }

        [Fact]
        public async Task RefineAsync_ReturnsOriginalOnServiceException()
        {
            // Arrange
            var failingClient = new TestChatClient(string.Empty, exception: new InvalidOperationException("Service unavailable"));
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ResumeMarkdownRefinement:Model", "openrouter/free" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<ResumeMarkdownRefinementService>>();

            var service = new ResumeMarkdownRefinementService(
                failingClient,
                loggerMock.Object,
                CreateLocalizerMock().Object,
                config);

            // Act
            var original = "## Original resume content";
            var result = await service.RefineAsync(original, "Test Resume");

            // Assert
            Assert.Equal(original, result);
        }

        private sealed class TestChatClient : IChatClient
        {
            private readonly string _responseContent;
            private readonly Exception? _exception;

            public TestChatClient(string responseContent, Exception? exception = null)
            {
                _responseContent = responseContent;
                _exception = exception;
            }

            public ChatOptions? CapturedOptions { get; private set; }

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                CapturedOptions = options;
                if (_exception is not null)
                {
                    throw _exception;
                }
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseContent)));
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
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
