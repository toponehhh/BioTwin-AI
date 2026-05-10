using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Dedicated service for generating text embeddings via LLM.
    /// Separated from AgentService to break circular dependency with RagService.
    /// </summary>
    public class EmbeddingService : IEmbeddingService
    {
        private readonly ILogger<EmbeddingService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _provider;
        private readonly string _llmBaseUrl;
        private readonly string _embeddingModel;
        private readonly string? _apiKey;

        public EmbeddingService(
            ILogger<EmbeddingService> logger,
            IConfiguration config,
            HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            _provider = config["LLM:Provider"] ?? "Ollama";
            _llmBaseUrl = config["LLM:BaseUrl"] ?? "http://localhost:11434";
            // Use a dedicated embedding model for Ollama; do not fall back to the chat model.
            _embeddingModel = config["LLM:EmbeddingModel"] ?? "nomic-embed-text";
            _apiKey = config["LLM:ApiKey"];
        }

        /// <summary>
        /// Generate embeddings for text using the configured LLM embedding model.
        /// </summary>
        public async Task<float[]> GetEmbeddingAsync(string text, int vectorSize = 768)
        {
            try
            {
                if (string.Equals(_provider, "Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallOllamaEmbeddingAsync(text, vectorSize);
                }

                return await CallOpenAiEmbeddingAsync(text, vectorSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding");
                throw;
            }
        }

        private async Task<float[]> CallOllamaEmbeddingAsync(string text, int vectorSize)
        {
            var url = $"{_llmBaseUrl.TrimEnd('/')}/api/embed";
            var payload = new
            {
                model = _embeddingModel,
                input = text
            };

            using var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ollama embedding request failed ({(int)response.StatusCode}): {raw}");
            }

            using var document = JsonDocument.Parse(raw);
            var embeddingsArray = document.RootElement.GetProperty("embeddings");

            if (embeddingsArray.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No embeddings returned from Ollama");
            }

            var embedding = new List<float>();
            foreach (var element in embeddingsArray[0].EnumerateArray())
            {
                embedding.Add(element.GetSingle());
            }

            return TrimOrPad(embedding, vectorSize);
        }

        private async Task<float[]> CallOpenAiEmbeddingAsync(string text, int vectorSize)
        {
            var baseUrl = _llmBaseUrl.TrimEnd('/');
            var url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseUrl}/embeddings"
                : $"{baseUrl}/v1/embeddings";

            var payload = new
            {
                model = _embeddingModel,
                input = text
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
                throw new InvalidOperationException($"OpenAI embedding request failed ({(int)response.StatusCode}): {raw}");
            }

            using var document = JsonDocument.Parse(raw);
            var data = document.RootElement.GetProperty("data");

            if (data.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No embeddings returned from OpenAI");
            }

            var embeddingArray = data[0].GetProperty("embedding");
            var embedding = new List<float>();
            foreach (var element in embeddingArray.EnumerateArray())
            {
                embedding.Add(element.GetSingle());
            }

            return TrimOrPad(embedding, vectorSize);
        }

        private static float[] TrimOrPad(List<float> embedding, int vectorSize)
        {
            var result = embedding.Take(vectorSize).ToArray();
            if (result.Length < vectorSize)
            {
                Array.Resize(ref result, vectorSize);
            }
            return result;
        }
    }
}
