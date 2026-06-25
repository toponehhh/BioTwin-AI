using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Rag;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class ChatApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IChatApiClient
{
    public Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<ChatResponse>(HttpMethod.Post, "api/chat", request, cancellationToken);
    }

    public Task<RagSearchResponse> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<RagSearchResponse>(HttpMethod.Post, "api/rag/search", request, cancellationToken);
    }
}
