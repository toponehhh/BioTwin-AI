namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public sealed class RerankResponseException(string message) : InvalidOperationException(message);
