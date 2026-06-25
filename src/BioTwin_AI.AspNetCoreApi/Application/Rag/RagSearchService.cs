using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Rag;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Rag;

public sealed class RagSearchService(
    BioTwinApiDbContext dbContext,
    IEmbeddingService embeddingService) : IRagSearchService
{
    public async Task<RagSearchResponse> SearchAsync(string tenantId, bool includeAllTenants, RagSearchRequest request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 20);
        var queryEmbedding = await embeddingService.EmbedAsync(request.Query, cancellationToken);

        var candidates = await dbContext.ResumeSectionVectors
            .AsNoTracking()
            .Include(vector => vector.ResumeSection)
            .Where(vector => includeAllTenants || vector.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var results = candidates
            .Select(vector =>
            {
                var score = Cosine(queryEmbedding, EmbeddingPayloadSerializer.Deserialize(vector.EmbeddingPayload));
                score += LexicalBoost(request.Query, $"{vector.ResumeTitle} {vector.SectionTitle} {vector.Content}");

                return new RagCitationDto(
                    vector.ResumeSection?.ResumeEntryId ?? 0,
                    vector.ResumeSectionId,
                    vector.ResumeTitle,
                    vector.SectionTitle,
                    Preview(vector.Content),
                    (float)Math.Clamp(score, 0d, 1d));
            })
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToArray();

        return new RagSearchResponse(results);
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
}
