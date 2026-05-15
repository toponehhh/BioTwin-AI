using BioTwin_AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
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
            var service = new EmbeddingService(loggerMock.Object, config, generator);

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
            var service = new EmbeddingService(loggerMock.Object, config, generator);

            var longMarkdown = string.Join("\n\n", Enumerable.Repeat(new string('x', 3000), 4));

            // Act
            var embedding = await service.GetEmbeddingAsync(longMarkdown, 768);

            // Assert
            Assert.Equal(768, embedding.Length);
            Assert.True(generator.RequestInputs.Count >= 2);
            Assert.All(generator.RequestInputs, input => Assert.True(input.Length <= 8000));
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
