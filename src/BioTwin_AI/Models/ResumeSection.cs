namespace BioTwin_AI.Models
{
    /// <summary>
    /// Represents one editable Markdown section parsed from a resume.
    /// </summary>
    public class ResumeSection
    {
        public int Id { get; set; }

        public int ResumeEntryId { get; set; }

        public ResumeEntry? ResumeEntry { get; set; }

        public int? ParentSectionId { get; set; }

        public ResumeSection? ParentSection { get; set; }

        public List<ResumeSection> ChildSections { get; set; } = new();

        public int HeadingLevel { get; set; } = 2;

        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string TenantId { get; set; } = "default";

        /// <summary>
        /// Serialized embedding payload for RAG retrieval.
        /// </summary>
        public string? EmbeddingPayload { get; set; }
    }
}
