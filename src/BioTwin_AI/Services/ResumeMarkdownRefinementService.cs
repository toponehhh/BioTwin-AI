using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using OllamaSharp;
using OllamaSharp.Models;
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
        private readonly bool _isOllamaProvider;
        private readonly string _model;
        private readonly bool _enabled;
        private readonly double _temperature;
        private readonly int _maxTokens;
        private readonly int _numPredict;
        private readonly int _numCtx;
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
            _isOllamaProvider = string.Equals(config["LLM:Provider"] ?? "Ollama", "Ollama", StringComparison.OrdinalIgnoreCase);
            _model = config["ResumeMarkdownRefinement:Model"] ?? config["LLM:Model"] ?? "gemma4:e2b";
            _enabled = config.GetValue("ResumeMarkdownRefinement:Enabled", true);
            _temperature = config.GetValue("ResumeMarkdownRefinement:Temperature", 0.1);
            _maxTokens = config.GetValue("ResumeMarkdownRefinement:MaxTokens", 3000);
            _numPredict = config.GetValue("ResumeMarkdownRefinement:NumPredict", 3000);
            _numCtx = config.GetValue("ResumeMarkdownRefinement:NumCtx", 8192);
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
You are a resume Markdown editor.
Rewrite converted resume Markdown into a clean, structured resume outline.

Rules:
- Return Markdown only. Do not wrap it in code fences.
- Preserve all factual content. Do not invent companies, dates, metrics, tools, education, or contact details.
- Improve hierarchy beyond only H1/H2 when useful. Use H1 for the resume/person title, H2 for major sections, H3 for roles/projects/education items, and H4 for nested details only when helpful.
- Keep bullets concise and grouped under the most relevant heading.
- Keep original language and wording as much as possible while removing obvious conversion noise.
- Do not add commentary, explanations, or placeholders.
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
            var maxOutputTokens = _isOllamaProvider ? _numPredict : _maxTokens;
            var options = new ChatOptions
            {
                ModelId = _model,
                Temperature = (float)_temperature,
                MaxOutputTokens = maxOutputTokens
            };

            if (_isOllamaProvider)
            {
                options.AddOllamaOption(OllamaOption.NumPredict, _numPredict);
                options.AddOllamaOption(OllamaOption.NumCtx, _numCtx);
                options.AddOllamaOption(OllamaOption.Think, false);
                options.Reasoning = new ReasoningOptions { Output = ReasoningOutput.None };
            }

            return options;
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
