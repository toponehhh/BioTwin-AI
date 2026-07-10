using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeConversionJobService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ResumeConversionJobService> logger) : IResumeConversionJobService
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ConversionJob> jobs = new(StringComparer.Ordinal);

    public async Task<ResumeConversionJobDto> StartAsync(
        string tenantId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        if (file.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException("Uploaded file is larger than the 10 MB limit.");
        }

        var apiUrl = GetApiUrl();
        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);

        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(memory.ToArray());
        if (!string.IsNullOrWhiteSpace(file.ContentType))
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        }

        form.Add(content, "file", file.FileName);
        var client = httpClientFactory.CreateClient("all2md");
        using var response = await client.PostAsync($"{apiUrl}/convert/jobs", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var all2MdJob = await ReadJobAsync(response, cancellationToken);

        var jobId = Guid.NewGuid().ToString("N");
        jobs[jobId] = new ConversionJob(
            tenantId,
            all2MdJob.JobId,
            file.FileName,
            file.ContentType,
            timeProvider.GetUtcNow());

        return Map(jobId, all2MdJob, file.FileName, file.ContentType);
    }

    public async Task<ResumeConversionJobDto?> GetAsync(
        string tenantId,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (!jobs.TryGetValue(jobId, out var job) ||
            !string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
        {
            return null;
        }

        if (job.TerminalResult is not null)
        {
            return job.TerminalResult;
        }

        var timeoutSeconds = configuration.GetValue("All2MD:TimeoutSeconds", 600);
        if (timeProvider.GetUtcNow() - job.StartedAt > TimeSpan.FromSeconds(timeoutSeconds))
        {
            job.TerminalResult = new ResumeConversionJobDto(
                jobId,
                ResumeConversionJobStatuses.Failed,
                100,
                "Document conversion timed out.",
                null,
                $"All2MD conversion did not complete within {timeoutSeconds} seconds.");
            logger.LogWarning(
                "All2MD job {All2MdJobId} for BioTwin job {JobId} exceeded {TimeoutSeconds} seconds.",
                job.All2MdJobId,
                jobId,
                timeoutSeconds);
            return job.TerminalResult;
        }

        var client = httpClientFactory.CreateClient("all2md");
        using var response = await client.GetAsync(
            $"{GetApiUrl()}/convert/jobs/{Uri.EscapeDataString(job.All2MdJobId)}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var all2MdJob = await ReadJobAsync(response, cancellationToken);
        var result = Map(jobId, all2MdJob, job.FileName, job.ContentType);
        if (result.Status is ResumeConversionJobStatuses.Completed or ResumeConversionJobStatuses.Failed)
        {
            job.TerminalResult = result;
        }

        return result;
    }

    private string GetApiUrl()
    {
        var apiUrl = configuration["All2MD:ApiUrl"];
        return string.IsNullOrWhiteSpace(apiUrl)
            ? throw new InvalidOperationException("All2MD:ApiUrl is not configured.")
            : apiUrl.TrimEnd('/');
    }

    private static async Task<All2MdJobResponse> ReadJobAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<All2MdJobResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidOperationException("All2MD returned an empty job response.");
    }

    private static ResumeConversionJobDto Map(
        string jobId,
        All2MdJobResponse job,
        string fallbackFileName,
        string? fallbackContentType)
    {
        var result = job.Status == ResumeConversionJobStatuses.Completed && job.Result is not null
            ? new ConvertedResumeFileDto(
                Path.GetFileNameWithoutExtension(job.Result.FileName ?? fallbackFileName),
                job.Result.FileName ?? fallbackFileName,
                job.Result.Content ?? string.Empty,
                DetectLanguage(job.Result.Content ?? string.Empty),
                IsDuplicate: false,
                ExistingResumeEntryId: null,
                ExistingResumeTitle: null)
            : null;
        var error = job.Status == ResumeConversionJobStatuses.Failed
            ? job.Error ?? job.Message
            : null;

        return new ResumeConversionJobDto(
            jobId,
            job.Status,
            Math.Clamp(job.Progress, 0, 100),
            job.Message,
            result,
            error);
    }

    private static string DetectLanguage(string markdown)
    {
        var cjk = 0;
        var latin = 0;
        foreach (var ch in markdown)
        {
            if ((ch >= '\u4e00' && ch <= '\u9fff') || (ch >= '\u3400' && ch <= '\u4dbf'))
            {
                cjk++;
            }
            else if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                latin++;
            }
        }

        return cjk > 0 && cjk >= latin
            ? ResumeLanguages.SimplifiedChinese
            : latin > 0
                ? ResumeLanguages.English
                : ResumeLanguages.SimplifiedChinese;
    }

    private sealed class ConversionJob(
        string tenantId,
        string all2MdJobId,
        string fileName,
        string? contentType,
        DateTimeOffset startedAt)
    {
        public string TenantId { get; } = tenantId;
        public string All2MdJobId { get; } = all2MdJobId;
        public string FileName { get; } = fileName;
        public string? ContentType { get; } = contentType;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public ResumeConversionJobDto? TerminalResult { get; set; }
    }

    private sealed record All2MdJobResponse(
        [property: JsonPropertyName("job_id")] string JobId,
        string Status,
        int Progress,
        string Message,
        All2MdJobResult? Result,
        string? Error);

    private sealed record All2MdJobResult(
        string? Filename,
        string? Content,
        [property: JsonPropertyName("content_type")] string? ContentType)
    {
        [JsonIgnore]
        public string? FileName => Filename;
    }
}
