using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeService
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetSummariesAsync(string tenantId, CancellationToken cancellationToken);

    Task<ResumeDetailDto?> GetDetailAsync(string tenantId, int resumeId, CancellationToken cancellationToken);

    Task<ConvertedResumeFileDto> ConvertUploadAsync(string tenantId, IFormFile file, CancellationToken cancellationToken);

    Task<ResumeDetailDto> SaveAsync(string tenantId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken);

    Task<ResumeDetailDto?> ReplaceMarkdownAsync(string tenantId, int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string tenantId, int resumeId, CancellationToken cancellationToken);

    Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(string tenantId, CancellationToken cancellationToken);

    Task<ResumeMarkdownExportDto?> ExportMarkdownAsync(string tenantId, int resumeId, CancellationToken cancellationToken);

    Task<(string FileName, string ContentType, byte[] Content)?> GetOriginalAsync(string tenantId, int resumeId, CancellationToken cancellationToken);
}
