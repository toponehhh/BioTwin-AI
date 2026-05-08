using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for AI agent interaction with LLM using RAG context.
    /// Supports Ollama and OpenAI-compatible chat endpoints.
    /// </summary>
    public class AgentService
    {
        private readonly IRagService _ragService;
        private readonly ILogger<AgentService> _logger;
        private readonly HttpClient _httpClient;
        private readonly CurrentUserSession _session;
        private readonly string _provider;
        private readonly string _llmBaseUrl;
        private readonly string _model;
        private readonly string? _apiKey;
        private readonly double _temperature;
        private readonly int _maxTokens;

        public AgentService(
            IRagService ragService,
            ILogger<AgentService> logger,
            IConfiguration config,
            HttpClient httpClient,
            CurrentUserSession session)
        {
            _ragService = ragService;
            _logger = logger;
            _httpClient = httpClient;
            _session = session;
            _provider = config["LLM:Provider"] ?? "Ollama";
            _llmBaseUrl = config["LLM:BaseUrl"] ?? "http://localhost:11434";
            _model = config["LLM:Model"] ?? "qwen2.5:7b";
            _apiKey = config["LLM:ApiKey"];
            _temperature = config.GetValue("LLM:Temperature", 0.2);
            _maxTokens = config.GetValue("LLM:MaxTokens", 800);
        }

        /// <summary>
        /// Process interview question and generate response based on resume RAG.
        /// </summary>
        public async Task<string> AnswerQuestionAsync(string question)
        {
            try
            {
                _logger.LogInformation("Processing question: {Question}", question);

                var relevantContent = await _ragService.SearchAsync(question, limit: 3);
                var context = BuildContext(relevantContent);

                string systemPrompt;
                if (_session.IsInterviewer)
                {
                    systemPrompt = """
You are an interview assistant helping the interviewer review candidate resumes.
Analyze the provided resume context from one or more candidates and answer questions about them.
When referencing a candidate, use the name/username shown in the context (e.g. "[username - title]").
Provide objective, factual summaries based solely on the resume data provided.
If context is insufficient, clearly state that the information is not available.
""";
                }
                else
                {
                    systemPrompt = """
You are a professional interview candidate assistant.
Answer in first-person as the candidate.
Only use the provided resume context when answering factual experience questions.
If context is insufficient, honestly say you do not have enough information.
Keep answers concise and interview-friendly.
""";
                }

                var userPrompt = $"""
Question:
{question}

Resume Context:
{context}
""";

                return await CallLlmAsync(systemPrompt, userPrompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to answer question with LLM");
                return "I apologize, but I encountered an error when calling the language model. Please verify LLM settings and try again.";
            }
        }

        private static string BuildContext(IEnumerable<(string content, double score)> relevantContent)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Top matched resume snippets:");

            foreach (var (content, score) in relevantContent)
            {
                contextBuilder.AppendLine($"- Relevance: {score:P0}");
                contextBuilder.AppendLine(content);
                contextBuilder.AppendLine();
            }

            return contextBuilder.ToString();
        }

        private async Task<string> CallLlmAsync(string systemPrompt, string userPrompt)
        {
            if (string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOllamaAsync(systemPrompt, userPrompt);
            }

            return await CallOpenAiCompatibleAsync(systemPrompt, userPrompt);
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
                    temperature = _temperature
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
            var content = document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? "The model returned an empty response."
                : content;
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
            if (choices.GetArrayLength() == 0)
            {
                return "The model returned no choices.";
            }

            var content = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? "The model returned an empty response."
                : content;
        }
    }
}
