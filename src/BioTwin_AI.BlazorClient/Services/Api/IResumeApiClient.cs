using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface IResumeApiClient
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> GetResumeAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<ConvertedResumeFileDto> ConvertUploadAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> SaveResumeAsync(SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> ReplaceMarkdownAsync(int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(CancellationToken cancellationToken = default);

    Task<ResumeMarkdownExportDto> ExportMarkdownAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<byte[]> ExportPdfAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadOriginalAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<RefineMarkdownResponse> RefineAsync(RefineMarkdownRequest request, CancellationToken cancellationToken = default);
}
