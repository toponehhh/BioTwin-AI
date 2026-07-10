using System.Text;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Http;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeImportJobProcessor(
    IResumeConversionJobService conversionJobService,
    IResumeService resumeService,
    IResumeWizardExtractionService extractionService,
    IConfiguration configuration)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ResumeImportProcessResult> ProcessWizardAsync(
        string tenantId,
        ResumeImportFile file,
        string language,
        string? title,
        Action<int, string, string> report,
        CancellationToken cancellationToken)
    {
        report(20, "inspect", "Inspecting the selected document...");
        var prepared = await PrepareAsync(tenantId, file, 25, 65, report, cancellationToken);
        var normalizedLanguage = ResumeLanguages.IsSupported(language)
            ? ResumeLanguages.Normalize(language)
            : prepared.File.DetectedLanguage;
        var markdown = prepared.File.Markdown;
        var merged = false;

        report(68, "merge", "Checking your existing resume timeline...");
        var summaries = await resumeService.GetSummariesAsync(tenantId, cancellationToken);
        if (summaries.Any(item => string.Equals(item.Language, normalizedLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            var merge = await resumeService.MergePreviewAsync(
                tenantId,
                new MergeResumeMarkdownRequest(
                    normalizedLanguage,
                    title ?? prepared.File.Title,
                    markdown,
                    file.FileName),
                cancellationToken);
            if (merge is not null)
            {
                markdown = merge.MergedMarkdown;
                title = merge.MergedTitle;
                merged = true;
            }
        }

        var extracted = await ExtractWithProgressAsync(
            new ExtractResumeWizardRequest(markdown, normalizedLanguage, title ?? prepared.File.Title),
            report,
            cancellationToken);
        return new ResumeImportProcessResult(
            prepared.ConversionSkipped,
            merged,
            extracted.Resume,
            null,
            markdown);
    }

    public async Task<ResumeImportProcessResult> ProcessWorkspaceAsync(
        string tenantId,
        IReadOnlyList<ResumeImportFile> files,
        Action<int, string, string> report,
        CancellationToken cancellationToken)
    {
        var results = new List<ConvertedResumeFileDto>(files.Count);
        var allSkipped = true;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var start = 30 + (index * 50 / files.Count);
            var end = 30 + ((index + 1) * 50 / files.Count);
            report(start, "inspect", $"Inspecting {file.FileName} ({index + 1} of {files.Count})...");
            var prepared = await PrepareAsync(tenantId, file, start, end, report, cancellationToken);
            allSkipped &= prepared.ConversionSkipped;
            results.Add(prepared.File);
        }

        report(88, "draft", "Preparing editable resume drafts...");
        return new ResumeImportProcessResult(allSkipped, false, null, results, null);
    }

    private async Task<PreparedFile> PrepareAsync(
        string tenantId,
        ResumeImportFile file,
        int rangeStart,
        int rangeEnd,
        Action<int, string, string> report,
        CancellationToken cancellationToken)
    {
        if (IsMarkdown(file))
        {
            report(rangeEnd, "prepare", "Reading Markdown content...");
            string markdown;
            try
            {
                markdown = StrictUtf8.GetString(file.Content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ResumeImportException(
                    "invalid_markdown_encoding",
                    "The Markdown file is not valid UTF-8.",
                    "Save the file as UTF-8 and choose it again.",
                    false,
                    exception);
            }

            return new PreparedFile(
                new ConvertedResumeFileDto(
                    Path.GetFileNameWithoutExtension(file.FileName),
                    file.FileName,
                    markdown,
                    DetectLanguage(markdown),
                    false,
                    null,
                    null),
                true);
        }

        report(rangeStart, "prepare", "Converting the document to editable content...");
        await using var stream = new MemoryStream(file.Content, writable: false);
        var formFile = new FormFile(stream, 0, file.Content.Length, "file", file.FileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = file.ContentType
        };
        var conversion = await conversionJobService.StartAsync(tenantId, formFile, cancellationToken);
        while (conversion.Status is ResumeConversionJobStatuses.Queued or ResumeConversionJobStatuses.Running)
        {
            var mapped = rangeStart + (int)Math.Round((rangeEnd - rangeStart) * conversion.Progress / 100d);
            report(mapped, "prepare", "Converting the document to editable content...");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            conversion = await conversionJobService.GetAsync(tenantId, conversion.JobId, cancellationToken)
                ?? throw new ResumeImportException(
                    "conversion_unavailable",
                    "Document conversion could not be continued.",
                    "Retry the import. If it fails again, convert the file to Markdown first.",
                    true);
        }

        if (conversion.Status == ResumeConversionJobStatuses.Failed || conversion.Result is null)
        {
            throw new ResumeImportException(
                "conversion_failed",
                "The document could not be converted.",
                "Retry the import. If it fails again, convert the file to Markdown first.",
                true);
        }

        report(rangeEnd, "prepare", "Document content is ready.");
        return new PreparedFile(conversion.Result, false);
    }

    private async Task<ExtractResumeWizardResponse> ExtractWithProgressAsync(
        ExtractResumeWizardRequest request,
        Action<int, string, string> report,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(
            0.01,
            configuration.GetValue("LLM:ExtractionTimeoutSeconds", 600d)));
        var progressInterval = TimeSpan.FromSeconds(Math.Max(
            0.01,
            configuration.GetValue("LLM:ExtractionProgressIntervalSeconds", 10d)));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        report(84, "structure", "Structuring editable resume information...");
        var extractionTask = extractionService.ExtractAsync(request, timeoutSource.Token);
        var progress = 84;
        try
        {
            while (!extractionTask.IsCompleted)
            {
                var delay = Task.Delay(progressInterval, timeoutSource.Token);
                var completed = await Task.WhenAny(extractionTask, delay);
                if (completed == extractionTask)
                {
                    break;
                }

                await delay;
                progress = Math.Min(98, progress + 2);
                report(progress, "structure", "Analyzing and organizing resume details...");
            }

            return await extractionTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new ResumeImportException(
                "extraction_timeout",
                "Structuring the resume took too long.",
                "Retry the import. If it times out again, use a shorter Markdown resume or try another model.",
                true);
        }
        catch (InvalidOperationException exception)
        {
            throw new ResumeImportException(
                "extraction_failed",
                "BioTwin could not structure the resume response.",
                "Retry the import. The model returned content that could not be used as resume data.",
                true,
                exception);
        }
    }

    private static bool IsMarkdown(ResumeImportFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || file.ContentType.Equals("text/markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectLanguage(string markdown)
    {
        var cjk = markdown.Count(ch => ch is >= '\u3400' and <= '\u9fff');
        var latin = markdown.Count(ch => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'));
        return cjk > 0 && cjk >= latin ? ResumeLanguages.SimplifiedChinese : ResumeLanguages.English;
    }

    private sealed record PreparedFile(ConvertedResumeFileDto File, bool ConversionSkipped);
}

public sealed record ResumeImportFile(string FileName, string ContentType, byte[] Content);

public sealed record ResumeImportProcessResult(
    bool ConversionSkipped,
    bool MergePerformed,
    ResumeWizardDto? WizardResult,
    IReadOnlyList<ConvertedResumeFileDto>? WorkspaceResults,
    string? SourceMarkdown);

public sealed class ResumeImportException(
    string code,
    string message,
    string recoveryHint,
    bool canRetry,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
    public string RecoveryHint { get; } = recoveryHint;
    public bool CanRetry { get; } = canRetry;
}
