namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public interface ILocalRerankServiceFactory
{
    IRerankService GetOrCreate();
}
