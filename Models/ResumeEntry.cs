namespace BioTwin_AI.Models
{
    /// <summary>
    /// Represents a resume entry or work experience document
    /// </summary>
    public class ResumeEntry
    {
        public int Id { get; set; }

        /// <summary>
        /// The title/category (e.g., "Education", "Experience", "Skills")
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The raw markdown content converted from uploaded file
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Original filename that was uploaded
        /// </summary>
        public string SourceFileName { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when this entry was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Vector ID in Qdrant for retrieval
        /// </summary>
        public string? VectorId { get; set; }
    }
}
