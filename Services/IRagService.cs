namespace BioTwin_AI.Services
{
    public interface IRagService
    {
        Task InitializeAsync();
        Task<string> CreateEmbeddingPayloadAsync(string content, Dictionary<string, string> metadata);
        Task<List<(string Content, double Score)>> SearchAsync(string query, int limit = 5);
        Task ClearAsync();
    }
}