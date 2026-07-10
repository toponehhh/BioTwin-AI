namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public interface ILocalEmbeddingServiceFactory
{
    IEmbeddingService GetOrCreate();
}
