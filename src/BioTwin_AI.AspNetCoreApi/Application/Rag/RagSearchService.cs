using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using BioTwin_AI.DotNetShared.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Application.Rag;

public sealed class RagSearchService(
    BioTwinApiDbContext dbContext,
    IEmbeddingService embeddingService,
    IRerankService rerankService,
    IOptions<RerankFailoverOptions> rerankOptions) : IRagSearchService
{
    public async Task<RagSearchResponse> SearchAsync(string tenantId, bool includeAllTenants, RagSearchRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 20);
        var candidateCount = Math.Clamp(
            Math.Max(limit, rerankOptions.Value.CandidateCount),
            limit,
            100);
        var queryEmbedding = await embeddingService.EmbedAsync(request.Query, cancellationToken);

        var candidates = await dbContext.ResumeSectionVectors
            .AsNoTracking()
            .Include(vector => vector.ResumeSection)
            .Where(vector => includeAllTenants || vector.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var coarseCandidates = candidates
            .Select(vector =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var score = Cosine(queryEmbedding, EmbeddingPayloadSerializer.Deserialize(vector.EmbeddingPayload));
                score += LexicalBoost(request.Query, $"{vector.ResumeTitle} {vector.SectionTitle} {vector.Content}");

                return new RagCandidate(
                    new RagCitationDto(
                        vector.ResumeSection?.ResumeEntryId ?? 0,
                        vector.ResumeSectionId,
                        vector.ResumeTitle,
                        vector.SectionTitle,
                        Preview(vector.Content),
                        (float)Math.Clamp(score, 0d, 1d)),
                    $"{vector.ResumeTitle}\n{vector.SectionTitle}\n{vector.Content}");
            })
            .OrderByDescending(candidate => candidate.Citation.Score)
            .Take(candidateCount)
            .ToArray();

        if (coarseCandidates.Length == 0)
        {
            return new RagSearchResponse([]);
        }

        var reranked = await rerankService.RerankAsync(
            request.Query,
            coarseCandidates.Select(candidate => candidate.RerankText).ToArray(),
            Math.Min(limit, coarseCandidates.Length),
            cancellationToken);
        var results = MapRerankedResults(coarseCandidates, reranked, limit);
        return new RagSearchResponse(results);
    }

    private static IReadOnlyList<RagCitationDto> MapRerankedResults(
        IReadOnlyList<RagCandidate> coarseCandidates,
        IReadOnlyList<RerankResult> reranked,
        int limit)
    {
        var seenIndices = new HashSet<int>();
        var rerankBatchIsValid = reranked.Count > 0 && reranked.All(result =>
            result.Index >= 0 &&
            result.Index < coarseCandidates.Count &&
            double.IsFinite(result.Score) &&
            seenIndices.Add(result.Index));
        if (!rerankBatchIsValid)
        {
            return coarseCandidates.Take(limit).Select(candidate => candidate.Citation).ToArray();
        }

        var validResults = reranked
            .Take(limit)
            .Select(result =>
            {
                var citation = coarseCandidates[result.Index].Citation;
                return citation with { Score = (float)Math.Clamp(result.Score, 0d, 1d) };
            })
            .ToArray();

        return validResults;
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var count = Math.Min(left.Count, right.Count);
        if (count == 0)
        {
            return 0;
        }

        var dot = 0d;
        var leftNorm = 0d;
        var rightNorm = 0d;
        for (var i = 0; i < count; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 0;
        }

        return (dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)) + 1d) / 2d;
    }

    private static double LexicalBoost(string query, string content)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var normalizedContent = content.ToLowerInvariant();
        var hits = query
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', ';', ':', '/', '\\', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .Count(term => term.Length > 1 && normalizedContent.Contains(term, StringComparison.Ordinal));

        return Math.Min(0.25d, hits * 0.04d);
    }

    private static string Preview(string content)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        return normalized.Length <= 360 ? normalized : normalized[..360] + "...";
    }

    private sealed record RagCandidate(RagCitationDto Citation, string RerankText);
}
