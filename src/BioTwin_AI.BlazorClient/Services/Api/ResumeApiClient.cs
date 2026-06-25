using BioTwin_AI.DotNetShared.Resumes;
using System.Net.Http.Json;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class ResumeApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IResumeApiClient
{
    public Task<IReadOnlyList<ResumeSummaryDto>> GetResumesAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<ResumeSummaryDto>>("api/resumes", cancellationToken);
    }

    public Task<ResumeDetailDto> GetResumeAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return GetAsync<ResumeDetailDto>($"api/resumes/{resumeId}", cancellationToken);
    }

    public async Task<ConvertedResumeFileDto> ConvertUploadAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        using var request = CreateCredentialedRequest(HttpMethod.Post, "api/resumes/upload/convert");
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(streamContent, "file", fileName);
        request.Content = form;

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConvertedResumeFileDto>(cancellationToken)
            ?? throw new InvalidOperationException("API returned an empty response.");
    }

    public Task<ResumeDetailDto> SaveResumeAsync(SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<ResumeDetailDto>(HttpMethod.Post, "api/resumes", request, cancellationToken);
    }

    public Task<ResumeDetailDto> ReplaceMarkdownAsync(int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<ResumeDetailDto>(HttpMethod.Put, $"api/resumes/{resumeId}/markdown", request, cancellationToken);
    }

    public Task DeleteAsync(int resumeId, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(HttpMethod.Delete, $"api/resumes/{resumeId}", null, cancellationToken);
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
}
