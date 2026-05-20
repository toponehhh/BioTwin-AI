using BioTwin_AI.Models;

namespace BioTwin_AI.Services
{
    public interface IRagService
    {
        Task InitializeAsync();
        Task<string> CreateEmbeddingPayloadAsync(string content, Dictionary<string, string> metadata);
        Task<string> CreateEmbeddingPayloadAsync(ResumeSectionChunk chunk);
        Task<List<(string Content, double Score)>> SearchAsync(string query, int limit = 5);
        Task<List<(string Content, double Score)>> SearchForChatAsync(string query, int limit = 5);
        Task ClearAsync();
    }
}
