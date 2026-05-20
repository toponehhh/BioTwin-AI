using System.Text.Json;
using System.Text.Json.Serialization;

namespace BioTwin_AI.Models;

/// <summary>
/// Metadata carried with every stored resume section chunk.
/// </summary>
public record ResumeSectionChunkMetadata
{
    [JsonPropertyName("sectionTitle")]
    public string SectionTitle { get; init; } = string.Empty;

    [JsonPropertyName("headingLevel")]
    public int HeadingLevel { get; init; }

    /// <summary>
    /// Title breadcrumb from the nearest h2 ancestor down to the current section (inclusive),
    /// e.g. ["Work Experience", "Google", "Senior Engineer"].
    /// </summary>
    [JsonPropertyName("titleBreadcrumb")]
    public IReadOnlyList<string> TitleBreadcrumb { get; init; } = Array.Empty<string>();

    [JsonPropertyName("resumeSectionId")]
    public int ResumeSectionId { get; init; }

    [JsonPropertyName("resumeEntryId")]
    public int ResumeEntryId { get; init; }

    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; init; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; init; } = string.Empty;
}

/// <summary>
/// Structured content stored in <see cref="ResumeSectionVector.Content"/>.
/// Combines the raw markdown chunk with rich hierarchical metadata so that
/// both the embedding model and the reranker receive full context.
/// </summary>
public record ResumeSectionChunk
{
    /// <summary>Raw markdown text of this section.</summary>
    [JsonPropertyName("chunk")]
    public string Chunk { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public ResumeSectionChunkMetadata Metadata { get; init; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Parse stored JSON content.  Falls back to treating the raw string as a
    /// plain-text chunk (backward compat for records created before this schema).
    /// </summary>
    public static ResumeSectionChunk Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ResumeSectionChunk();
        }

        if (content.TrimStart().StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ResumeSectionChunk>(content, JsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch
            {
                // fall through to plain-text fallback
            }
        }

        return new ResumeSectionChunk { Chunk = content };
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Text fed to the embedding model: "Title Path: A > B > C\n\nContent:\n{chunk}".
    /// Produces richer semantic signal than plain text alone.
    /// </summary>
    public string ToEmbeddingText()
    {
        var parts = new List<string>();

        if (Metadata.TitleBreadcrumb.Count > 0)
        {
            parts.Add($"Title Path: {string.Join(" > ", Metadata.TitleBreadcrumb)}");
        }
        else if (!string.IsNullOrWhiteSpace(Metadata.SectionTitle))
        {
            parts.Add($"Section Title: {Metadata.SectionTitle}");
        }

        if (!string.IsNullOrWhiteSpace(Chunk))
        {
            parts.Add($"Content:\n{Chunk}");
        }

        return string.Join("\n\n", parts);
    }
}
