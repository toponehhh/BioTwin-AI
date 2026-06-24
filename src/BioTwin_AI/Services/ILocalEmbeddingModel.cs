namespace BioTwin_AI.Services
{
    public interface ILocalEmbeddingModel
    {
        float[] GenerateEmbedding(string text);
    }
}
