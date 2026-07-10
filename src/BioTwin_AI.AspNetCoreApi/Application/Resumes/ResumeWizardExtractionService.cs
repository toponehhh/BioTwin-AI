using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeWizardExtractionService(
    ILlmChatService llmChatService,
    ILogger<ResumeWizardExtractionService> logger,
    IConfiguration? configuration = null) : IResumeWizardExtractionService
{
    private const int MaxExtractionAttempts = 2;
    private const int DefaultExtractionMaxTokens = 8000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ExtractResumeWizardResponse> ExtractAsync(
        ExtractResumeWizardRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            throw new InvalidOperationException("Resume Markdown cannot be empty.");
        }

        if (!ResumeLanguages.IsSupported(request.Language))
        {
            throw new InvalidOperationException($"Unsupported resume language '{request.Language}'.");
        }

        JsonException? lastJsonException = null;
        LlmResponseException? lastProviderException = null;
        var receivedContent = false;
        var extractionMaxTokens = Math.Max(
            1000,
            configuration?.GetValue("LLM:ExtractionMaxTokens", DefaultExtractionMaxTokens)
                ?? DefaultExtractionMaxTokens);
        for (var attempt = 1; attempt <= MaxExtractionAttempts; attempt++)
        {
            string response;
            try
            {
                response = await llmChatService.CompleteAsync(
                    BuildMessages(request),
                    new ChatOptions
                    {
                        Temperature = 0.1f,
                        MaxOutputTokens = extractionMaxTokens,
                        Reasoning = new ReasoningOptions
                        {
                            Effort = ReasoningEffort.Low,
                            Output = ReasoningOutput.None
                        },
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<ResumeWizardDto>(
                            JsonOptions,
                            "resume_wizard",
                            "Structured resume information extracted from the supplied Markdown.")
                    },
                    LlmRequestKind.StructuredExtraction,
                    cancellationToken);
            }
            catch (LlmResponseException ex)
            {
                lastProviderException = ex;
                logger.LogWarning(
                    ex,
                    "Resume wizard extraction providers returned no usable content on attempt {Attempt} of {MaxAttempts}.",
                    attempt,
                    MaxExtractionAttempts);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(response))
            {
                receivedContent = true;
                try
                {
                    var resume = JsonSerializer.Deserialize<ResumeWizardDto>(ExtractJsonPayload(response), JsonOptions)
                        ?? throw new JsonException("Resume extraction returned an empty JSON value.");
                    return new ExtractResumeWizardResponse(Normalize(resume, request), []);
                }
                catch (JsonException ex)
                {
                    lastJsonException = ex;
                    logger.LogWarning(
                        ex,
                        "Resume wizard extraction returned invalid JSON on attempt {Attempt} of {MaxAttempts}.",
                        attempt,
                        MaxExtractionAttempts);
                    continue;
                }
            }

            logger.LogWarning(
                "Resume wizard extraction returned empty content on attempt {Attempt} of {MaxAttempts}.",
                attempt,
                MaxExtractionAttempts);
        }

        if (!receivedContent)
        {
            if (lastProviderException is not null)
            {
                throw new InvalidOperationException(
                    $"Resume extraction providers failed after {MaxExtractionAttempts} attempts.",
                    lastProviderException);
            }

            throw new InvalidOperationException(
                $"Resume extraction returned no content after {MaxExtractionAttempts} attempts.");
        }

        throw new InvalidOperationException("Resume extraction returned invalid JSON.", lastJsonException);
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(ExtractResumeWizardRequest request)
    {
        var requestedLanguage = string.Equals(request.Language, ResumeLanguages.English, StringComparison.OrdinalIgnoreCase)
            ? "English"
            : "Simplified Chinese";
        var systemPrompt = $$"""
You are a senior resume information extractor.
Extract only facts present in the supplied Markdown. Never invent employers, roles, dates, education, projects, technologies, contact details, or achievements.
Preserve experience order and all meaningful achievement bullets.
The resume language is {{requestedLanguage}}.
Return one JSON object only, without Markdown fences or commentary, using this exact shape:
{
  "title": "",
  "language": "{{ResumeLanguages.Normalize(request.Language)}}",
  "profile": { "fullName": "", "email": "", "location": "", "website": null },
  "summary": "",
  "experiences": [{ "company": "", "role": "", "startDate": "", "endDate": null, "highlights": [] }],
  "education": [{ "institution": "", "qualification": "", "startDate": "", "endDate": null }],
  "skills": [],
  "projects": [{ "name": "", "description": "", "technologies": [] }],
  "additionalSections": []
}
Use empty strings or arrays when a fact is absent.
""";
        var userPrompt = $"""
Suggested title: {request.Title ?? string.Empty}

Resume Markdown:
{request.Markdown}
""";

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        ];
    }

    private static ResumeWizardDto Normalize(ResumeWizardDto resume, ExtractResumeWizardRequest request)
    {
        var language = ResumeLanguages.Normalize(request.Language);
        var profile = resume.Profile ?? new ResumeWizardProfileDto(string.Empty, string.Empty, string.Empty, null);
        var title = FirstNonBlank(resume.Title, request.Title, profile.FullName, language == ResumeLanguages.English ? "Resume" : "个人简历");

        return new ResumeWizardDto(
            title,
            language,
            new ResumeWizardProfileDto(
                Clean(profile.FullName),
                Clean(profile.Email),
                Clean(profile.Location),
                CleanOptional(profile.Website)),
            Clean(resume.Summary),
            (resume.Experiences ?? [])
                .Select(item => new ResumeWizardExperienceDto(
                    Clean(item.Company),
                    Clean(item.Role),
                    Clean(item.StartDate),
                    CleanOptional(item.EndDate),
                    (item.Highlights ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).ToArray()))
                .ToArray(),
            (resume.Education ?? [])
                .Select(item => new ResumeWizardEducationDto(
                    Clean(item.Institution),
                    Clean(item.Qualification),
                    Clean(item.StartDate),
                    CleanOptional(item.EndDate)))
                .ToArray(),
            (resume.Skills ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            (resume.Projects ?? [])
                .Select(item => new ResumeWizardProjectDto(
                    Clean(item.Name),
                    Clean(item.Description),
                    (item.Technologies ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).ToArray()))
                .ToArray(),
            (resume.AdditionalSections ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(Clean).ToArray());
    }

    private static string StripCodeFence(string? response)
    {
        var json = (response ?? string.Empty).Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            json = json[7..].Trim();
        }
        else if (json.StartsWith("```", StringComparison.Ordinal))
        {
            json = json[3..].Trim();
        }

        if (json.EndsWith("```", StringComparison.Ordinal))
        {
            json = json[..^3].Trim();
        }

        return json;
    }

    private static string ExtractJsonPayload(string? response)
    {
        var content = StripCodeFence(response);
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start
            ? content[start..(end + 1)]
            : content;
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonBlank(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
}
