namespace BioTwin_AI.Models
{
    /// <summary>
    /// Stores the embedding and metadata for a resume section in a dedicated table.
    /// </summary>
    public class ResumeSectionVector
    {
        public int Id { get; set; }

        public int ResumeSectionId { get; set; }

        public ResumeSection? ResumeSection { get; set; }

        public string TenantId { get; set; } = "default";

        public string SectionTitle { get; set; } = string.Empty;

        public string? ParentSectionTitle { get; set; }

        public string Content { get; set; } = string.Empty;

        public string EmbeddingPayload { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
