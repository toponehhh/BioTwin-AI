using BioTwin_AI.BlazorClient.Models;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface IResumeApiClient
{
    Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default);

    Task<ResumeStateDto> GetResumeStateAsync(CancellationToken cancellationToken = default);

    Task<ResumeOperationLeaseDto> AcquireOperationAsync(AcquireResumeOperationRequest request, CancellationToken cancellationToken = default);

    Task<ResumeOperationLeaseDto> HeartbeatOperationAsync(string operationId, CancellationToken cancellationToken = default);

    Task ReleaseOperationAsync(string operationId, CancellationToken cancellationToken = default);

    Task<ResumeImportJobDto> StartWizardImportAsync(PendingResumeUpload upload, string operationId, string expectedStateToken, string language, string? title, CancellationToken cancellationToken = default);

    Task<ResumeImportJobDto> StartWorkspaceImportAsync(IReadOnlyList<PendingResumeUpload> uploads, string operationId, string expectedStateToken, CancellationToken cancellationToken = default);

    Task<ResumeImportJobDto> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default);

    Task CancelImportJobAsync(string jobId, string operationId, CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> GetResumeAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> SaveResumeAsync(SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default);

    Task<ResumeDetailDto> ReplaceMarkdownAsync(int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default);

    Task<MergeResumeMarkdownResponse> MergePreviewAsync(MergeResumeMarkdownRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(int resumeId, string operationId, string expectedStateToken, CancellationToken cancellationToken = default);

    Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(CancellationToken cancellationToken = default);

    Task<ResumeMarkdownExportDto> ExportMarkdownAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<byte[]> ExportPdfAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadOriginalAsync(int resumeId, CancellationToken cancellationToken = default);

    Task<RefineMarkdownResponse> RefineAsync(RefineMarkdownRequest request, CancellationToken cancellationToken = default);
}
