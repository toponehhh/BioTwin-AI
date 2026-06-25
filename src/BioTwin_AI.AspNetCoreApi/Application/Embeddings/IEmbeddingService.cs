namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}
