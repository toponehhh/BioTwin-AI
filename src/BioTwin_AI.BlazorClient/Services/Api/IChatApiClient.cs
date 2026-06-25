using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Rag;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface IChatApiClient
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default);

    Task<RagSearchResponse> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
}
