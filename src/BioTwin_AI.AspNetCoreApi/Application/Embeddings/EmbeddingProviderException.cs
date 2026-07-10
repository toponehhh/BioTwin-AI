namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class EmbeddingProviderException(string message) : InvalidOperationException(message);
