using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class EmbeddingServiceTests
    {
        [Fact]
        public async Task GetEmbeddingAsync_UsesOllamaNomicEmbedTextAndParsesEmbedding()
        {
            // Arrange
            var responseJson = """
            {
              "embeddings": [[0.1, 0.2, 0.3]],
              "model": "nomic-embed-text"
            }
            """;

            var handler = new RecordingHandler(responseJson);
            var httpClient = new HttpClient(handler);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, config, httpClient);

            // Act
            var embedding = await service.GetEmbeddingAsync("candidate experience", 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.Equal(0.1f, embedding[0], 6);
            Assert.Equal(0.2f, embedding[1], 6);
            Assert.Equal(0.3f, embedding[2], 6);

            Assert.NotNull(handler.LastRequest);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("http://localhost:11434/api/embed", handler.LastRequest.RequestUri!.ToString());

            using var requestJson = JsonDocument.Parse(handler.LastBody!);
            Assert.Equal("nomic-embed-text", requestJson.RootElement.GetProperty("model").GetString());
            Assert.Equal("candidate experience", requestJson.RootElement.GetProperty("input").GetString());
        }

        [Fact]
        public async Task GetEmbeddingAsync_LongMarkdown_SplitsIntoChunksForOllama()
        {
            // Arrange
            var handler = new ChunkAwareHandler(maxInputLength: 8000);
            var httpClient = new HttpClient(handler);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:Provider", "Ollama" },
                    { "LLM:BaseUrl", "http://localhost:11434" }
                })
                .Build();

            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, config, httpClient);

            var longMarkdown = string.Join("\n\n", Enumerable.Repeat(new string('中', 3000), 4));

            // Act
            var embedding = await service.GetEmbeddingAsync(longMarkdown, 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.True(handler.RequestInputs.Count >= 2);
            Assert.All(handler.RequestInputs, input => Assert.True(input.Length <= 8000));
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string _responseJson;

            public RecordingHandler(string responseJson)
            {
                _responseJson = responseJson;
            }

            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed class ChunkAwareHandler : HttpMessageHandler
        {
            private readonly int _maxInputLength;

            public ChunkAwareHandler(int maxInputLength)
            {
                _maxInputLength = maxInputLength;
            }

            public List<string> RequestInputs { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
                using var json = JsonDocument.Parse(body);
                var input = json.RootElement.GetProperty("input").GetString() ?? string.Empty;
                RequestInputs.Add(input);

                if (input.Length > _maxInputLength)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"error\":\"the input length exceeds the context length\"}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"embeddings\":[[0.1,0.2,0.3]],\"model\":\"nomic-embed-text\"}", Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
