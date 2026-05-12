namespace BioTwin_AI.Models
{
    /// <summary>
    /// Represents one uploaded resume source file.
    /// </summary>
    public class ResumeEntry
    {
        public int Id { get; set; }

        /// <summary>
        /// User-facing title entered when this resume was imported or created.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Original filename that was uploaded
        /// </summary>
        public string SourceFileName { get; set; } = string.Empty;

        /// <summary>
        /// Original uploaded file bytes stored as a database BLOB.
        /// </summary>
        public byte[]? SourceFileContent { get; set; }

        /// <summary>
        /// MIME type reported for the uploaded source file.
        /// </summary>
        public string? SourceContentType { get; set; }

        /// <summary>
        /// Original uploaded file size in bytes.
        /// </summary>
        public long? SourceFileSize { get; set; }

        /// <summary>
        /// SHA-256 hash of the original uploaded file bytes.
        /// </summary>
        public string? SourceFileHash { get; set; }

        /// <summary>
        /// Timestamp when this entry was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tenant identifier for data isolation.
        /// </summary>
        public string TenantId { get; set; } = "default";

        /// <summary>
        /// Sections parsed from this source resume.
        /// </summary>
        public List<ResumeSection> Sections { get; set; } = new();
    }
}
