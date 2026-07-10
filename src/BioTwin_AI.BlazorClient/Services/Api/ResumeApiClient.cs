using BioTwin_AI.BlazorClient.Models;
using BioTwin_AI.DotNetShared.Resumes;
using System.Net.Http.Json;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class ResumeApiClient(HttpClient httpClient, ILogger<ResumeApiClient> logger)
    : ApiClientBase(httpClient, logger), IResumeApiClient
{
    public Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<ResumeSummaryDto>>("api/resumes", cancellationToken);
    }

    public Task<ResumeDetailDto> GetResumeAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return GetAsync<ResumeDetailDto>($"api/resumes/{resumeId}", cancellationToken);
    }

    public Task<ResumeStateDto> GetResumeStateAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ResumeStateDto>("api/resumes/state", cancellationToken);

    public Task<ResumeOperationLeaseDto> AcquireOperationAsync(
        AcquireResumeOperationRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<ResumeOperationLeaseDto>(HttpMethod.Post, "api/resumes/operations", request, cancellationToken);

    public Task<ResumeOperationLeaseDto> HeartbeatOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<ResumeOperationLeaseDto>(
            HttpMethod.Put,
            $"api/resumes/operations/{Uri.EscapeDataString(operationId)}/heartbeat",
            null,
            cancellationToken);

    public Task ReleaseOperationAsync(string operationId, CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            HttpMethod.Delete,
            $"api/resumes/operations/{Uri.EscapeDataString(operationId)}",
            null,
            cancellationToken);

    public Task<ResumeImportJobDto> StartWizardImportAsync(
        PendingResumeUpload upload,
        string operationId,
        string expectedStateToken,
        string language,
        string? title,
        CancellationToken cancellationToken = default)
    {
        return StartImportAsync(
            "api/resumes/import-jobs/wizard",
            [upload],
            operationId,
            expectedStateToken,
            language,
            title,
            cancellationToken);
    }

    public Task<ResumeImportJobDto> StartWorkspaceImportAsync(
        IReadOnlyList<PendingResumeUpload> uploads,
        string operationId,
        string expectedStateToken,
        CancellationToken cancellationToken = default)
    {
        return StartImportAsync(
            "api/resumes/import-jobs/workspace",
            uploads,
            operationId,
            expectedStateToken,
            null,
            null,
            cancellationToken);
    }

    public Task<ResumeImportJobDto> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default) =>
        GetAsync<ResumeImportJobDto>($"api/resumes/import-jobs/{Uri.EscapeDataString(jobId)}", cancellationToken);

    public Task CancelImportJobAsync(
        string jobId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            HttpMethod.Delete,
            $"api/resumes/import-jobs/{Uri.EscapeDataString(jobId)}?operationId={Uri.EscapeDataString(operationId)}",
            null,
            cancellationToken);

    public Task<ResumeDetailDto> SaveResumeAsync(SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<ResumeDetailDto>(HttpMethod.Post, "api/resumes", request, cancellationToken);
    }

    public Task<ResumeDetailDto> ReplaceMarkdownAsync(int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<ResumeDetailDto>(HttpMethod.Put, $"api/resumes/{resumeId}/markdown", request, cancellationToken);
    }

    public Task<MergeResumeMarkdownResponse> MergePreviewAsync(MergeResumeMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<MergeResumeMarkdownResponse>(HttpMethod.Post, "api/resumes/merge-preview", request, cancellationToken);
    }

    public Task DeleteAsync(int resumeId, string operationId, string expectedStateToken, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(
            HttpMethod.Delete,
            $"api/resumes/{resumeId}?operationId={Uri.EscapeDataString(operationId)}&expectedStateToken={Uri.EscapeDataString(expectedStateToken)}",
            null,
            cancellationToken);
    }

    public Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<RebuildEmbeddingsResponse>(HttpMethod.Post, "api/resumes/rebuild-embeddings", null, cancellationToken);
    }

    public Task<ResumeMarkdownExportDto> ExportMarkdownAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return GetAsync<ResumeMarkdownExportDto>($"api/resumes/{resumeId}/export/markdown", cancellationToken);
    }

    public Task<byte[]> ExportPdfAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return GetBytesAsync($"api/resumes/{resumeId}/export/pdf", cancellationToken);
    }

    public Task<byte[]> DownloadOriginalAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return GetBytesAsync($"api/resumes/{resumeId}/original", cancellationToken);
    }

    public Task<RefineMarkdownResponse> RefineAsync(RefineMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<RefineMarkdownResponse>(HttpMethod.Post, "api/resumes/refine", request, cancellationToken);
    }

    private async Task<ResumeImportJobDto> StartImportAsync(
        string uri,
        IReadOnlyList<PendingResumeUpload> uploads,
        string operationId,
        string expectedStateToken,
        string? language,
        string? title,
        CancellationToken cancellationToken)
    {
        using var request = CreateCredentialedRequest(HttpMethod.Post, uri);
        using var form = new MultipartFormDataContent();
        var fileFieldName = uri.EndsWith("/wizard", StringComparison.Ordinal) ? "file" : "files";
        foreach (var upload in uploads)
        {
            var content = new ByteArrayContent(upload.Content);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType);
            form.Add(content, fileFieldName, upload.FileName);
        }

        form.Add(new StringContent(operationId), "operationId");
        form.Add(new StringContent(expectedStateToken), "expectedStateToken");
        if (!string.IsNullOrWhiteSpace(language))
        {
            form.Add(new StringContent(language), "language");
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            form.Add(new StringContent(title), "title");
        }

        request.Content = form;
        using var response = await SendLoggedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ResumeImportJobDto>(cancellationToken)
            ?? throw new InvalidOperationException("API returned an empty resume import job response.");
    }
}
