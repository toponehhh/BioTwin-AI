namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class EmbeddingResponseException(string message) : InvalidOperationException(message);
