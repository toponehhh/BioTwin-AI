using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using System.Text.RegularExpressions;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Uses the configured chat model to clean and deepen All2MD resume Markdown structure.
    /// </summary>
    public class ResumeMarkdownRefinementService
    {
        private readonly IChatClient _chatClient;
        private readonly ILogger<ResumeMarkdownRefinementService> _logger;
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly string _model;
        private readonly bool _enabled;
        private readonly double _temperature;
        private readonly int _maxTokens;
        private readonly int _maxInputChars;

        public ResumeMarkdownRefinementService(
            IChatClient chatClient,
            ILogger<ResumeMarkdownRefinementService> logger,
            IStringLocalizer<SharedResource> localizer,
            IConfiguration config)
        {
            _chatClient = chatClient;
            _logger = logger;
            _localizer = localizer;

            var configuredModel = config["ResumeMarkdownRefinement:Model"];
            _model = string.IsNullOrWhiteSpace(configuredModel) || string.Equals(configuredModel, "auto", StringComparison.OrdinalIgnoreCase)
                ? config["LLM:Model"] ?? "openrouter/free"
                : configuredModel.Trim();

            _enabled = config.GetValue("ResumeMarkdownRefinement:Enabled", true);
            _temperature = config.GetValue("ResumeMarkdownRefinement:Temperature", 0.1);
            _maxTokens = config.GetValue("ResumeMarkdownRefinement:MaxTokens", 3000);
            _maxInputChars = config.GetValue("ResumeMarkdownRefinement:MaxInputChars", 24000);
        }

        public async Task<string> RefineAsync(string markdown, string resumeTitle, Func<string, Task>? progress = null)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(markdown))
            {
                return markdown;
            }

            try
            {
                await ReportProgressAsync(progress, T("RefiningMarkdownStructure", _model));

                var normalizedMarkdown = NormalizeInput(markdown);
                if (normalizedMarkdown.Length > _maxInputChars)
                {
                    _logger.LogWarning(
                        "Resume Markdown refinement skipped because input length {Length} exceeds limit {Limit}.",
                        normalizedMarkdown.Length,
                        _maxInputChars);
                    await ReportProgressAsync(progress, T("MarkdownIsTooLargeForRefinement"));
                    return markdown;
                }

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = BuildUserPrompt(resumeTitle, normalizedMarkdown);
                var refinedMarkdown = await RefineWithChatClientAsync(systemPrompt, userPrompt);

                refinedMarkdown = CleanModelMarkdown(refinedMarkdown);
                if (string.IsNullOrWhiteSpace(refinedMarkdown))
                {
                    _logger.LogWarning("Resume Markdown refinement returned empty content. Using original Markdown.");
                    return markdown;
                }

                await ReportProgressAsync(progress, T("MarkdownStructureRefined"));
                return refinedMarkdown;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resume Markdown refinement failed. Using original Markdown.");
                await ReportProgressAsync(progress, T("MarkdownRefinementUnavailable"));
                return markdown;
            }
        }

        private string NormalizeInput(string markdown)
        {
            return markdown.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static string BuildSystemPrompt()
        {
            return """
You are a resume Markdown cleanup assistant.
The input is converted resume Markdown that may contain noisy symbols, broken paragraphs, repeated separators, and formatting artifacts.
Your job is to rewrite it into a clean, structured resume outline that is easy to read, preserves all factual content, and is optimized for downstream embedding chunking.

Rules:
- Return Markdown only. Do not wrap output in code fences.
- Preserve all original facts. Do not invent companies, dates, metrics, skills, education, or contact details.
- Remove meaningless symbols, stray characters, duplicate separators, inline noise, and obvious conversion artifacts.
- Organize sections with clear headings and concise bullets.
- Rewrite long text into clean, short paragraphs and bullet groups to make the content easier to segment for embeddings.
- Keep the resume structure logical and readable, without adding commentary or analysis.
""";
        }

        private static string BuildUserPrompt(string resumeTitle, string markdown)
        {
            return $"""
Resume title:
{resumeTitle}

Converted Markdown:
{markdown}
""";
        }

        private async Task<string> RefineWithChatClientAsync(string systemPrompt, string userPrompt)
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            };

            var response = await _chatClient.GetResponseAsync(messages, CreateChatOptions());
            return response.Text ?? string.Empty;
        }

        private ChatOptions CreateChatOptions()
        {
            return new ChatOptions
            {
                ModelId = _model,
                Temperature = (float)_temperature,
                MaxOutputTokens = _maxTokens
            };
        }

        private static string CleanModelMarkdown(string markdown)
        {
            var cleaned = markdown.Trim();
            cleaned = Regex.Replace(cleaned, @"^```(?:markdown|md)?\s*", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*```$", string.Empty);
            return cleaned.Trim();
        }

        private static Task ReportProgressAsync(Func<string, Task>? progress, string message)
        {
            return progress is null ? Task.CompletedTask : progress(message);
        }

        private string T(string key)
        {
            return _localizer[key].Value;
        }

        private string T(string key, params object[] arguments)
        {
            return _localizer[key, arguments].Value;
        }
    }
}
