using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BioTwin_AI.Services
{
    public sealed record AgentStreamChunk(string Kind, string Content)
    {
        public const string Answer = "answer";
        public const string Thinking = "thinking";
    }

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
        private readonly int _maxContextChars;
        private readonly int _maxSnippetChars;
        private readonly int _chatNumPredict;
        private readonly int _chatNumCtx;
        private readonly bool _ollamaThink;

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
            _model = config["LLM:Model"] ?? "gemma4:e2b";
            _apiKey = config["LLM:ApiKey"];
            _temperature = config.GetValue("LLM:Temperature", 0.2);
            _maxTokens = config.GetValue("LLM:MaxTokens", 800);
            _maxContextChars = config.GetValue("LLM:MaxContextChars", 6000);
            _maxSnippetChars = config.GetValue("LLM:MaxSnippetChars", 2000);
            _chatNumPredict = config.GetValue("LLM:ChatNumPredict", 256);
            _chatNumCtx = config.GetValue("LLM:ChatNumCtx", 2048);
            _ollamaThink = config.GetValue("LLM:Think", false);
        }

        /// <summary>
        /// Process interview question and generate response based on resume RAG.
        /// </summary>
        public async Task<string> AnswerQuestionAsync(string question)
        {
            try
            {
                var builder = new StringBuilder();

                await foreach (var chunk in StreamAnswerQuestionAsync(question))
                {
                    builder.Append(chunk);
                }

                return builder.Length == 0
                    ? "The model returned an empty response."
                    : builder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to answer question with LLM");
                return "I apologize, but I encountered an error when calling the language model. Please verify LLM settings and try again.";
            }
        }

        /// <summary>
        /// Stream answer chunks for the provided question.
        /// </summary>
        public async IAsyncEnumerable<string> StreamAnswerQuestionAsync(
            string question,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamAnswerQuestionChunksAsync(question, cancellationToken))
            {
                if (chunk.Kind == AgentStreamChunk.Answer)
                {
                    yield return chunk.Content;
                }
            }
        }

        /// <summary>
        /// Stream answer and thinking chunks for the provided question.
        /// </summary>
        public async IAsyncEnumerable<AgentStreamChunk> StreamAnswerQuestionChunksAsync(
            string question,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Processing question: {Question}", question);

            var relevantContent = await _ragService.SearchAsync(question, limit: 3);
            var context = BuildContext(relevantContent);

            var (systemPrompt, userPrompt) = BuildPrompts(question, context);

            if (string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                var emittedAnswer = false;
                await foreach (var chunk in StreamOllamaAsync(systemPrompt, userPrompt, _ollamaThink, _chatNumPredict, _chatNumCtx, cancellationToken))
                {
                    if (chunk.Kind == AgentStreamChunk.Answer)
                    {
                        emittedAnswer = true;
                    }

                    yield return chunk;
                }

                if (!emittedAnswer && _ollamaThink && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Ollama returned no visible content with thinking enabled. Retrying once with thinking disabled.");
                    await foreach (var chunk in StreamOllamaAsync(systemPrompt, userPrompt, false, _chatNumPredict, _chatNumCtx, cancellationToken))
                    {
                        yield return chunk;
                    }
                }
            }
            else
            {
                yield return new AgentStreamChunk(AgentStreamChunk.Answer, await CallOpenAiCompatibleAsync(systemPrompt, userPrompt));
            }
        }

        private string BuildContext(IEnumerable<(string content, double score)> relevantContent)
        {
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Top matched resume snippets:");
            var remaining = _maxContextChars;

            foreach (var (content, score) in relevantContent)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var snippet = content;
                if (snippet.Length > _maxSnippetChars)
                {
                    snippet = snippet[.._maxSnippetChars] + "\n...[truncated]";
                }

                if (snippet.Length > remaining)
                {
                    if (remaining > 64)
                    {
                        snippet = snippet[..remaining] + "\n...[truncated]";
                    }
                    else
                    {
                        break;
                    }
                }

                contextBuilder.AppendLine($"- Relevance: {score:P0}");
                contextBuilder.AppendLine(snippet);
                contextBuilder.AppendLine();

                remaining = _maxContextChars - contextBuilder.Length;
            }

            return contextBuilder.ToString();
        }

        private (string SystemPrompt, string UserPrompt) BuildPrompts(string question, string context)
        {
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

            return (systemPrompt, userPrompt);
        }

        private async Task<string> CallOllamaAsync(string systemPrompt, string userPrompt)
        {
            var url = $"{_llmBaseUrl.TrimEnd('/')}/api/chat";
            var payload = new
            {
                model = _model,
                stream = false,
                think = _ollamaThink,
                options = new
                {
                    temperature = _temperature,
                    num_predict = _chatNumPredict,
                    num_ctx = _chatNumCtx
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

        private async IAsyncEnumerable<AgentStreamChunk> StreamOllamaAsync(
            string systemPrompt,
            string userPrompt,
            bool think,
            int numPredict,
            int numCtx,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var url = $"{_llmBaseUrl.TrimEnd('/')}/api/chat";
            var payload = new
            {
                model = _model,
                stream = true,
                think,
                options = new
                {
                    temperature = _temperature,
                    num_predict = numPredict,
                    num_ctx = numCtx
                },
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

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var rawError = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Ollama request failed ({(int)response.StatusCode}): {rawError}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new StringBuilder();

            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (buffer.Length > 0)
                {
                    buffer.AppendLine();
                }

                buffer.Append(line);

                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(buffer.ToString());
                    buffer.Clear();
                }
                catch (JsonException)
                {
                    // Keep buffering until we have a complete JSON object.
                    continue;
                }

                var root = document.RootElement;

                if (root.TryGetProperty("error", out var errorNode))
                {
                    document.Dispose();
                    throw new InvalidOperationException($"Ollama stream error: {errorNode.GetString()}");
                }

                if (root.TryGetProperty("message", out var messageNode)
                    && messageNode.TryGetProperty("thinking", out var thinkingNode))
                {
                    var piece = thinkingNode.GetString();
                    if (!string.IsNullOrEmpty(piece))
                    {
                        document.Dispose();
                        yield return new AgentStreamChunk(AgentStreamChunk.Thinking, piece);
                        continue;
                    }
                }

                if (root.TryGetProperty("message", out messageNode)
                    && messageNode.TryGetProperty("content", out var contentNode))
                {
                    var piece = contentNode.GetString();
                    if (!string.IsNullOrEmpty(piece))
                    {
                        document.Dispose();
                        yield return new AgentStreamChunk(AgentStreamChunk.Answer, piece);
                        continue;
                    }
                }

                if (root.TryGetProperty("done", out var doneNode) && doneNode.GetBoolean())
                {
                    document.Dispose();
                    yield break;
                }

                document.Dispose();
            }
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
