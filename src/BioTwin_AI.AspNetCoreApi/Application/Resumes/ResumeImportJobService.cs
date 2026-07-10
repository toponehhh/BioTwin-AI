using System.Collections.Concurrent;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeImportJobService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ResumeImportJobService> logger) : IResumeImportJobService
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, ImportJobState> jobs = new(StringComparer.Ordinal);

    public Task<ResumeImportJobDto> StartWizardAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        IFormFile file,
        string language,
        string? title,
        CancellationToken cancellationToken)
    {
        return StartAsync(
            userId,
            tenantId,
            operationId,
            expectedStateToken,
            "wizard",
            [file],
            language,
            title,
            cancellationToken);
    }

    public Task<ResumeImportJobDto> StartWorkspaceAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        return StartAsync(
            userId,
            tenantId,
            operationId,
            expectedStateToken,
            "workspace",
            files,
            null,
            null,
            cancellationToken);
    }

    public Task<ResumeImportJobDto?> GetAsync(string tenantId, string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            jobs.TryGetValue(jobId, out var job)
            && string.Equals(job.TenantId, tenantId, StringComparison.Ordinal)
                ? job.Snapshot
                : null);
    }

    public Task<bool> CancelAsync(string tenantId, string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!jobs.TryGetValue(jobId, out var job)
            || !string.Equals(job.TenantId, tenantId, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        job.Cancel();
        return Task.FromResult(true);
    }

    private async Task<ResumeImportJobDto> StartAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        string mode,
        IReadOnlyList<IFormFile> files,
        string? language,
        string? title,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new ResumeImportException("file_required", "Choose at least one resume file.", "Choose a file and try again.", false);
        }

        await using var validationScope = scopeFactory.CreateAsyncScope();
        await validationScope.ServiceProvider.GetRequiredService<IResumeOperationService>()
            .ValidateAsync(userId, tenantId, operationId, expectedStateToken, cancellationToken);

        var copiedFiles = new List<ResumeImportFile>(files.Count);
        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > MaxUploadBytes)
            {
                throw new ResumeImportException(
                    "invalid_file_size",
                    "Each resume file must be between 1 byte and 10 MB.",
                    "Choose a smaller file and try again.",
                    false);
            }

            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            copiedFiles.Add(new ResumeImportFile(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                buffer.ToArray()));
        }

        var jobId = Guid.NewGuid().ToString("N");
        var state = new ImportJobState(jobId, tenantId, mode, timeProvider.GetUtcNow());
        jobs[jobId] = state;
        _ = ProcessAsync(state, copiedFiles, language, title);
        return state.Snapshot;
    }

    private async Task ProcessAsync(
        ImportJobState job,
        IReadOnlyList<ResumeImportFile> files,
        string? language,
        string? title)
    {
        try
        {
            job.Update(15, "upload", "Document upload received.");
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<ResumeImportJobProcessor>();
            var result = job.Mode == "wizard"
                ? await processor.ProcessWizardAsync(
                    job.TenantId,
                    files[0],
                    language ?? ResumeLanguages.SimplifiedChinese,
                    title,
                    job.Update,
                    job.Token)
                : await processor.ProcessWorkspaceAsync(job.TenantId, files, job.Update, job.Token);
            job.Complete(result);
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            job.MarkCanceled();
        }
        catch (ResumeImportException exception)
        {
            logger.LogWarning(
                "Resume import job {JobId} failed with error code {ErrorCode}: {ErrorMessage}",
                job.JobId,
                exception.Code,
                exception.Message);
            job.Fail(exception.Code, exception.Message, exception.RecoveryHint, exception.CanRetry);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Resume import job {JobId} failed.", job.JobId);
            job.Fail(
                "resume_import_failed",
                "BioTwin could not finish processing this resume.",
                "Retry the import. If it fails again, choose a Markdown file or review the API logs.",
                true);
        }
    }

    private sealed class ImportJobState
    {
        private readonly object sync = new();
        private readonly CancellationTokenSource cancellation = new();
        private ResumeImportJobDto snapshot;

        public ImportJobState(string jobId, string tenantId, string mode, DateTimeOffset startedAt)
        {
            JobId = jobId;
            TenantId = tenantId;
            Mode = mode;
            StartedAt = startedAt;
            snapshot = new ResumeImportJobDto(
                jobId,
                ResumeImportJobStatuses.Queued,
                0,
                "upload",
                "Waiting to process the selected document...",
                BuildStages(mode, "upload", false, false, false),
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                false);
        }

        public string JobId { get; }
        public string TenantId { get; }
        public string Mode { get; }
        public DateTimeOffset StartedAt { get; }
        public CancellationToken Token => cancellation.Token;

        public ResumeImportJobDto Snapshot
        {
            get { lock (sync) { return snapshot; } }
        }

        public void Update(int progress, string stage, string message)
        {
            lock (sync)
            {
                if (IsTerminal(snapshot.Status))
                {
                    return;
                }

                snapshot = snapshot with
                {
                    Status = ResumeImportJobStatuses.Running,
                    Progress = Math.Clamp(progress, 0, 99),
                    CurrentStage = stage,
                    Message = message,
                    Stages = BuildStages(Mode, stage, false, false, false)
                };
            }
        }

        public void Complete(ResumeImportProcessResult result)
        {
            lock (sync)
            {
                if (IsTerminal(snapshot.Status))
                {
                    return;
                }

                snapshot = snapshot with
                {
                    Status = ResumeImportJobStatuses.Completed,
                    Progress = 100,
                    CurrentStage = Mode == "wizard" ? "structure" : "draft",
                    Message = "Resume processing completed.",
                    Stages = BuildStages(Mode, string.Empty, true, !result.MergePerformed, false),
                    ConversionSkipped = result.ConversionSkipped,
                    WizardResult = result.WizardResult,
                    WorkspaceResults = result.WorkspaceResults,
                    SourceMarkdown = result.SourceMarkdown
                };
            }
        }

        public void Fail(string code, string message, string recoveryHint, bool canRetry)
        {
            lock (sync)
            {
                if (snapshot.Status == ResumeImportJobStatuses.Canceled)
                {
                    return;
                }

                snapshot = snapshot with
                {
                    Status = ResumeImportJobStatuses.Failed,
                    Message = "Resume processing stopped.",
                    Stages = BuildStages(Mode, snapshot.CurrentStage, false, false, true),
                    ErrorCode = code,
                    Error = message,
                    RecoveryHint = recoveryHint,
                    CanRetry = canRetry
                };
            }
        }

        public void Cancel()
        {
            cancellation.Cancel();
            MarkCanceled();
        }

        public void MarkCanceled()
        {
            lock (sync)
            {
                if (IsTerminal(snapshot.Status))
                {
                    return;
                }

                snapshot = snapshot with
                {
                    Status = ResumeImportJobStatuses.Canceled,
                    Message = "Resume import canceled.",
                    ErrorCode = null,
                    Error = null,
                    RecoveryHint = null,
                    CanRetry = false
                };
            }
        }

        private static IReadOnlyList<ResumeImportStageDto> BuildStages(
            string mode,
            string current,
            bool completed,
            bool mergeSkipped,
            bool failed)
        {
            var definitions = mode == "wizard"
                ? new[] { ("upload", "Upload"), ("inspect", "Inspect"), ("prepare", "Prepare"), ("merge", "Merge"), ("structure", "Structure") }
                : new[] { ("upload", "Upload"), ("inspect", "Inspect"), ("prepare", "Prepare"), ("draft", "Draft") };
            var currentIndex = Array.FindIndex(definitions, item => item.Item1 == current);
            return definitions.Select((item, index) =>
            {
                var status = completed
                    ? item.Item1 == "merge" && mergeSkipped ? "skipped" : "completed"
                    : index < currentIndex ? "completed"
                    : index == currentIndex ? failed ? "failed" : "active"
                    : "pending";
                var progress = status is "completed" or "skipped" ? 100 : status == "active" ? 50 : 0;
                return new ResumeImportStageDto(item.Item1, item.Item2, status, progress);
            }).ToArray();
        }

        private static bool IsTerminal(string status) =>
            status is ResumeImportJobStatuses.Completed or ResumeImportJobStatuses.Failed or ResumeImportJobStatuses.Canceled;
    }
}
