using System.Text;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeImportJobServiceTests
{
    [Fact]
    public async Task Wizard_markdown_import_skips_conversion_and_runs_merge_and_extraction_in_api()
    {
        var conversion = new StubConversionService();
        var resumes = new StubResumeService(hasCanonicalResume: true);
        var extraction = new StubExtractionService();
        var service = CreateService(conversion, resumes, extraction);

        var started = await service.StartWizardAsync(
            7,
            "huangd",
            "operation-1",
            "state-1",
            CreateFile("resume.md", "text/markdown", "# Donald\n\n## 2026\nBuilt AI systems."),
            ResumeLanguages.English,
            "Donald",
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(service, "huangd", started.JobId);

        Assert.Equal(ResumeImportJobStatuses.Completed, completed.Status);
        Assert.True(completed.ConversionSkipped);
        Assert.Equal(0, conversion.StartCount);
        Assert.Equal(1, resumes.MergeCount);
        Assert.Equal(1, extraction.CallCount);
        Assert.NotNull(completed.WizardResult);
        Assert.Contains("Merged", completed.SourceMarkdown, StringComparison.Ordinal);
        Assert.All(completed.Stages, stage => Assert.Contains(stage.Status, new[] { "completed", "skipped" }));
    }

    [Fact]
    public async Task Workspace_markdown_import_returns_draft_without_extraction()
    {
        var conversion = new StubConversionService();
        var extraction = new StubExtractionService();
        var service = CreateService(conversion, new StubResumeService(false), extraction);

        var started = await service.StartWorkspaceAsync(
            7,
            "huangd",
            "operation-1",
            "state-1",
            [CreateFile("resume.markdown", "application/octet-stream", "# Resume")],
            CancellationToken.None);
        var completed = await WaitForTerminalAsync(service, "huangd", started.JobId);

        Assert.Equal(ResumeImportJobStatuses.Completed, completed.Status);
        Assert.Single(completed.WorkspaceResults!);
        Assert.Equal("# Resume", completed.WorkspaceResults![0].Markdown);
        Assert.Equal(0, conversion.StartCount);
        Assert.Equal(0, extraction.CallCount);
    }

    [Fact]
    public async Task Canceled_job_cannot_be_overwritten_by_late_processor_completion()
    {
        var extraction = new BlockingExtractionService();
        var service = CreateService(
            new StubConversionService(),
            new StubResumeService(false),
            extraction);
        var started = await service.StartWizardAsync(
            7,
            "huangd",
            "operation-1",
            "state-1",
            CreateFile("resume.md", "text/markdown", "# Resume"),
            ResumeLanguages.English,
            null,
            CancellationToken.None);
        await extraction.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(await service.CancelAsync("huangd", started.JobId, CancellationToken.None));
        extraction.Complete();
        await Task.Delay(50);
        var result = await service.GetAsync("huangd", started.JobId, CancellationToken.None);

        Assert.Equal(ResumeImportJobStatuses.Canceled, result?.Status);
    }

    [Fact]
    public async Task Wizard_structure_progress_advances_while_extraction_is_running()
    {
        var extraction = new BlockingExtractionService();
        var service = CreateService(
            new StubConversionService(),
            new StubResumeService(false),
            extraction,
            new Dictionary<string, string?>
            {
                ["LLM:ExtractionTimeoutSeconds"] = "5",
                ["LLM:ExtractionProgressIntervalSeconds"] = "0.02"
            });
        var started = await service.StartWizardAsync(
            7,
            "huangd",
            "operation-1",
            "state-1",
            CreateFile("resume.md", "text/markdown", "# Resume"),
            ResumeLanguages.English,
            null,
            CancellationToken.None);
        await extraction.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        ResumeImportJobDto? running = null;
        var progressDeadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < progressDeadline)
        {
            running = await service.GetAsync("huangd", started.JobId, CancellationToken.None);
            if (running?.Progress > 84)
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.Equal(ResumeImportJobStatuses.Running, running?.Status);
        Assert.InRange(running!.Progress, 85, 98);
        await service.CancelAsync("huangd", started.JobId, CancellationToken.None);
    }

    [Fact]
    public async Task Wizard_structure_timeout_returns_actionable_failure()
    {
        var extraction = new BlockingExtractionService();
        var service = CreateService(
            new StubConversionService(),
            new StubResumeService(false),
            extraction,
            new Dictionary<string, string?>
            {
                ["LLM:ExtractionTimeoutSeconds"] = "0.05",
                ["LLM:ExtractionProgressIntervalSeconds"] = "0.01"
            });
        var started = await service.StartWizardAsync(
            7,
            "huangd",
            "operation-1",
            "state-1",
            CreateFile("resume.md", "text/markdown", "# Resume"),
            ResumeLanguages.English,
            null,
            CancellationToken.None);

        var failed = await WaitForTerminalAsync(service, "huangd", started.JobId);

        Assert.Equal(ResumeImportJobStatuses.Failed, failed.Status);
        Assert.Equal("extraction_timeout", failed.ErrorCode);
        Assert.True(failed.CanRetry);
        Assert.Contains("took too long", failed.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ResumeImportJobService CreateService(
        IResumeConversionJobService conversion,
        IResumeService resumes,
        IResumeWizardExtractionService extraction,
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => conversion);
        services.AddScoped(_ => resumes);
        services.AddScoped(_ => extraction);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues ?? new Dictionary<string, string?>())
            .Build());
        services.AddScoped<IResumeOperationService, StubOperationService>();
        services.AddScoped<ResumeImportJobProcessor>();
        var provider = services.BuildServiceProvider();
        return new ResumeImportJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<ResumeImportJobService>.Instance);
    }

    private static async Task<ResumeImportJobDto> WaitForTerminalAsync(
        IResumeImportJobService service,
        string tenantId,
        string jobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var job = await service.GetAsync(tenantId, jobId, CancellationToken.None);
            Assert.NotNull(job);
            if (job!.Status is ResumeImportJobStatuses.Completed or ResumeImportJobStatuses.Failed or ResumeImportJobStatuses.Canceled)
            {
                return job;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Import job did not complete.");
    }

    private static IFormFile CreateFile(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private sealed class StubConversionService : IResumeConversionJobService
    {
        public int StartCount { get; private set; }

        public Task<ResumeConversionJobDto> StartAsync(string tenantId, IFormFile file, CancellationToken cancellationToken)
        {
            StartCount++;
            throw new InvalidOperationException("Markdown must bypass conversion.");
        }

        public Task<ResumeConversionJobDto?> GetAsync(string tenantId, string jobId, CancellationToken cancellationToken) =>
            Task.FromResult<ResumeConversionJobDto?>(null);
    }

    private sealed class StubResumeService(bool hasCanonicalResume) : IResumeService
    {
        public int MergeCount { get; private set; }

        public Task<IReadOnlyList<ResumeSummaryDto>> GetSummariesAsync(string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ResumeSummaryDto>>(hasCanonicalResume
                ? [new ResumeSummaryDto(1, "Existing", ResumeLanguages.English, null, DateTimeOffset.UtcNow, 1, false)]
                : []);

        public Task<MergeResumeMarkdownResponse?> MergePreviewAsync(string tenantId, MergeResumeMarkdownRequest request, CancellationToken cancellationToken)
        {
            MergeCount++;
            return Task.FromResult<MergeResumeMarkdownResponse?>(new(
                request.Language,
                1,
                request.DraftTitle,
                $"# Merged\n\n{request.DraftMarkdown}",
                []));
        }

        public Task<ResumeDetailDto?> GetDetailAsync(string tenantId, int resumeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConvertedResumeFileDto> ConvertUploadAsync(string tenantId, IFormFile file, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeDetailDto> SaveAsync(string tenantId, SaveResumeMarkdownRequest request, int? userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeDetailDto?> ReplaceMarkdownAsync(string tenantId, int resumeId, SaveResumeMarkdownRequest request, int? userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string tenantId, int resumeId, int? userId, string? operationId, string? expectedStateToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(string tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeMarkdownExportDto?> ExportMarkdownAsync(string tenantId, int resumeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string FileName, string ContentType, byte[] Content)?> GetOriginalAsync(string tenantId, int resumeId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubExtractionService : IResumeWizardExtractionService
    {
        public int CallCount { get; private set; }

        public Task<ExtractResumeWizardResponse> ExtractAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ExtractResumeWizardResponse(
                new ResumeWizardDto(
                    request.Title ?? "Resume",
                    request.Language,
                    new ResumeWizardProfileDto("Donald", string.Empty, string.Empty, null),
                    string.Empty,
                    [],
                    [],
                    [],
                    [],
                    []),
                []));
        }
    }

    private sealed class BlockingExtractionService : IResumeWizardExtractionService
    {
        private readonly TaskCompletionSource<ExtractResumeWizardResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExtractResumeWizardResponse> ExtractAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public void Complete()
        {
            completion.TrySetResult(new ExtractResumeWizardResponse(
                new ResumeWizardDto(
                    "Resume",
                    ResumeLanguages.English,
                    new ResumeWizardProfileDto(string.Empty, string.Empty, string.Empty, null),
                    string.Empty,
                    [],
                    [],
                    [],
                    [],
                    []),
                []));
        }
    }

    private sealed class StubOperationService : IResumeOperationService
    {
        public Task<ResumeOperationLeaseDto> AcquireAsync(int userId, string tenantId, string operationType, string expectedStateToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ResumeOperationLeaseDto> HeartbeatAsync(int userId, string operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ValidateAsync(int userId, string tenantId, string operationId, string expectedStateToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
