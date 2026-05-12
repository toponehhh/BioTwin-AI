using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Uses the configured chat model to clean and deepen All2MD resume Markdown structure.
    /// </summary>
    public class ResumeMarkdownRefinementService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ResumeMarkdownRefinementService> _logger;
        private readonly string _provider;
        private readonly string _llmBaseUrl;
        private readonly string _model;
        private readonly string? _apiKey;
        private readonly bool _enabled;
        private readonly double _temperature;
        private readonly int _maxTokens;
        private readonly int _numPredict;
        private readonly int _numCtx;
        private readonly int _maxInputChars;

        public ResumeMarkdownRefinementService(
            HttpClient httpClient,
            ILogger<ResumeMarkdownRefinementService> logger,
            IConfiguration config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _provider = config["LLM:Provider"] ?? "Ollama";
            _llmBaseUrl = config["LLM:BaseUrl"] ?? "http://localhost:11434";
            _model = config["ResumeMarkdownRefinement:Model"] ?? config["LLM:Model"] ?? "gemma4:e2b";
            _apiKey = config["LLM:ApiKey"];
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
                await ReportProgressAsync(progress, $"Refining Markdown structure with {_model}...");

                var normalizedMarkdown = NormalizeInput(markdown);
                if (normalizedMarkdown.Length > _maxInputChars)
                {
                    _logger.LogWarning(
                        "Resume Markdown refinement skipped because input length {Length} exceeds limit {Limit}.",
                        normalizedMarkdown.Length,
                        _maxInputChars);
                    await ReportProgressAsync(progress, "Markdown is too large for automatic refinement. Using the original conversion result.");
                    return markdown;
                }

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = BuildUserPrompt(resumeTitle, normalizedMarkdown);

                var refinedMarkdown = string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase)
                    ? await CallOllamaAsync(systemPrompt, userPrompt)
                    : await CallOpenAiCompatibleAsync(systemPrompt, userPrompt);

                refinedMarkdown = CleanModelMarkdown(refinedMarkdown);
                if (string.IsNullOrWhiteSpace(refinedMarkdown))
                {
                    _logger.LogWarning("Resume Markdown refinement returned empty content. Using original Markdown.");
                    return markdown;
                }

                await ReportProgressAsync(progress, "Markdown structure refined. Preparing preview...");
                return refinedMarkdown;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resume Markdown refinement failed. Using original Markdown.");
                await ReportProgressAsync(progress, "Markdown refinement was unavailable. Using the original conversion result.");
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

        private async Task<string> CallOllamaAsync(string systemPrompt, string userPrompt)
        {
            var url = $"{_llmBaseUrl.TrimEnd('/')}/api/chat";
            var payload = new
            {
                model = _model,
                stream = false,
                options = new
                {
                    temperature = _temperature,
                    num_predict = _numPredict,
                    num_ctx = _numCtx
                },
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ollama request failed ({(int)response.StatusCode}): {raw}");
            }

            using var document = JsonDocument.Parse(raw);
            return document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }

        private async Task<string> CallOpenAiCompatibleAsync(string systemPrompt, string userPrompt)
        {
            var baseUrl = _llmBaseUrl.TrimEnd('/');
            var url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/chat/completions"
                : $"{baseUrl}/v1/chat/completions";

            var payload = new
            {
                model = _model,
                temperature = _temperature,
                max_tokens = _maxTokens,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            using var response = await _httpClient.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI-compatible request failed ({(int)response.StatusCode}): {raw}");
            }

            using var document = JsonDocument.Parse(raw);
            var choices = document.RootElement.GetProperty("choices");
            return choices.GetArrayLength() == 0
                ? string.Empty
                : choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
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
    }
}
