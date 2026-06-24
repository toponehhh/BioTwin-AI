using BioTwin_AI.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class EmbeddingServiceTests
    {
        [Fact]
        public async Task GetEmbeddingAsync_UsesLocalEmbeddingModelAndReturnsRequestedVectorSize()
        {
            var model = new RecordingLocalEmbeddingModel(_ => new[] { 0.1f, 0.2f, 0.3f });
            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, model);

            var embedding = await service.GetEmbeddingAsync("candidate experience", 768);

            Assert.Equal(768, embedding.Length);
            Assert.Equal(0.1f, embedding[0], 6);
            Assert.Equal(0.2f, embedding[1], 6);
            Assert.Equal(0.3f, embedding[2], 6);
            Assert.All(embedding.Skip(3), value => Assert.Equal(0f, value));
            Assert.Equal("candidate experience", model.RequestInputs.Single());
        }

        [Fact]
        public async Task GetEmbeddingAsync_LongMarkdown_SplitsIntoChunksBeforeLocalEmbedding()
        {
            var model = new RecordingLocalEmbeddingModel(_ => new[] { 1f, 1f, 1f });
            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, model);
            var longMarkdown = string.Join("\n\n", Enumerable.Repeat(new string('x', 3000), 4));

            var embedding = await service.GetEmbeddingAsync(longMarkdown, 768);

            Assert.Equal(768, embedding.Length);
            Assert.True(model.RequestInputs.Count >= 2);
            Assert.All(model.RequestInputs, input => Assert.True(input.Length <= 8000));
        }

        [Fact]
        public async Task GetEmbeddingAsync_LocalEmbeddingLongerThanRequestedSize_TrimsVector()
        {
            var model = new RecordingLocalEmbeddingModel(_ => new[] { 0.1f, 0.2f, 0.3f });
            var loggerMock = new Mock<ILogger<EmbeddingService>>();
            var service = new EmbeddingService(loggerMock.Object, model);

            var embedding = await service.GetEmbeddingAsync("candidate experience", 2);

            Assert.Equal(new[] { 0.1f, 0.2f }, embedding);
        }

        private sealed class RecordingLocalEmbeddingModel : ILocalEmbeddingModel
        {
            private readonly Func<string, float[]> _embeddingFactory;

            public RecordingLocalEmbeddingModel(Func<string, float[]> embeddingFactory)
            {
                _embeddingFactory = embeddingFactory;
            }

            public List<string> RequestInputs { get; } = new();

            public float[] GenerateEmbedding(string text)
            {
                RequestInputs.Add(text);
                return _embeddingFactory(text);
            }
        }
    }
}
