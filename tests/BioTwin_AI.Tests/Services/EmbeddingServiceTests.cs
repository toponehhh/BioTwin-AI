using BioTwin_AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class EmbeddingServiceTests
    {
        [Fact]
        public async Task GetEmbeddingAsync_UsesConfiguredEmbeddingModelAndReturnsVector()
        {
            // Arrange
            var generator = new RecordingEmbeddingGenerator();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:EmbeddingModel", "nomic-embed-text" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient());

            var service = new EmbeddingService(loggerMock.Object, config, generator, httpClientFactoryMock.Object);

            // Act
            var embedding = await service.GetEmbeddingAsync("candidate experience", 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.Equal(0.1f, embedding[0], 6);
            Assert.Equal(0.2f, embedding[1], 6);
            Assert.Equal(0.3f, embedding[2], 6);

            Assert.Equal("candidate experience", generator.RequestInputs.Single());
            Assert.Equal("nomic-embed-text", generator.LastOptions?.ModelId);
            Assert.Equal(768, generator.LastOptions?.Dimensions);
        }

        [Fact]
        public async Task GetEmbeddingAsync_LongMarkdown_SplitsIntoChunksBeforeEmbedding()
        {
            // Arrange
            var generator = new RecordingEmbeddingGenerator(maxInputLength: 8000);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:EmbeddingModel", "nomic-embed-text" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient());

            var service = new EmbeddingService(loggerMock.Object, config, generator, httpClientFactoryMock.Object);

            var longMarkdown = string.Join("\n\n", Enumerable.Repeat(new string('x', 3000), 4));

            // Act
            var embedding = await service.GetEmbeddingAsync(longMarkdown, 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.True(generator.RequestInputs.Count >= 2);
            Assert.All(generator.RequestInputs, input => Assert.True(input.Length <= 8000));
        }

        [Fact]
        public async Task GetEmbeddingAsync_AutoEmbeddingModel_UsesOpenRouterFreeModel()
        {
            // Arrange
            var generator = new RecordingEmbeddingGenerator();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:EmbeddingModel", "auto" }
                })
                .Build();

            var jsonPayload = "{\"data\":[{\"id\":\"nvidia/llama-nemotron-embed-vl-1b-v2:free\",\"canonical_slug\":\"llama-nemotron-embed-vl-1b-v2:free\",\"name\":\"Llama Nemotron Embed VL 1B V2 (free)\",\"output_modalities\":[\"embeddings\"],\"pricing\":{\"prompt\":0.0,\"completion\":0.0,\"embedding\":0.0,\"request\":0.0}}]}";
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var httpClient = new HttpClient(new FakeHttpMessageHandler(jsonPayload, HttpStatusCode.OK));
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, config, generator, httpClientFactoryMock.Object);

            // Act
            var embedding = await service.GetEmbeddingAsync("candidate experience", 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.Equal("nvidia/llama-nemotron-embed-vl-1b-v2:free", generator.LastOptions?.ModelId);
            Assert.Equal(768, generator.LastOptions?.Dimensions);
        }

        [Fact]
        public async Task GetEmbeddingAsync_AutoEmbeddingModel_FallsBackWhenDiscoveryFails()
        {
            // Arrange
            var generator = new RecordingEmbeddingGenerator();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:EmbeddingModel", "auto" }
                })
                .Build();

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var httpClient = new HttpClient(new FakeHttpMessageHandler("{\"invalid\":true}", HttpStatusCode.InternalServerError));
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, config, generator, httpClientFactoryMock.Object);

            // Act
            var embedding = await service.GetEmbeddingAsync("candidate experience", 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.Equal("nvidia/llama-nemotron-embed-vl-1b-v2:free", generator.LastOptions?.ModelId);
        }

        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseContent;
            private readonly HttpStatusCode _statusCode;

            public FakeHttpMessageHandler(string responseContent, HttpStatusCode statusCode)
            {
                _responseContent = responseContent;
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }

        private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
        {
            private readonly int _maxInputLength;

            public RecordingEmbeddingGenerator(int maxInputLength = int.MaxValue)
            {
                _maxInputLength = maxInputLength;
            }

            public List<string> RequestInputs { get; } = new();
            public EmbeddingGenerationOptions? LastOptions { get; private set; }

            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values,
                EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                LastOptions = options;

                var result = new GeneratedEmbeddings<Embedding<float>>();
                foreach (var value in values)
                {
                    RequestInputs.Add(value);
                    if (value.Length > _maxInputLength)
                    {
                        throw new InvalidOperationException("input length exceeds the context length");
                    }

                    var vector = new ReadOnlyMemory<float>(new[] { 0.1f, 0.2f, 0.3f });
                    result.Add(new Embedding<float>(vector));
                }

                return Task.FromResult(result);
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
