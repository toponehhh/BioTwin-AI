namespace BioTwin_AI.Services
{
    /// <summary>
    /// Interface for text embedding service via LLM.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generate embeddings for text using the configured LLM embedding model.
        /// </summary>
        /// <param name="text">Text to embed</param>
        /// <param name="vectorSize">Expected vector size (default 768)</param>
        /// <returns>Embedding vector as float array</returns>
        Task<float[]> GetEmbeddingAsync(string text, int vectorSize = 768);
    }
}
