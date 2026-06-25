using BioTwin_AI.DotNetShared.Rag;

namespace BioTwin_AI.AspNetCoreApi.Application.Rag;

public interface IRagSearchService
{
    Task<RagSearchResponse> SearchAsync(string tenantId, bool includeAllTenants, RagSearchRequest request, CancellationToken cancellationToken);
}
