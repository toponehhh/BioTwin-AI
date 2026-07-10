namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public interface IRerankService
{
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record RerankResult(int Index, double Score);
