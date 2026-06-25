using BioTwin_AI.DotNetShared.Chat;

namespace BioTwin_AI.AspNetCoreApi.Application.Chat;

public interface IChatService
{
    Task<ChatResponse> AskAsync(string tenantId, bool includeAllTenants, ChatRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ChatStreamChunk> StreamAsync(string tenantId, bool includeAllTenants, ChatRequest request, CancellationToken cancellationToken);
}
