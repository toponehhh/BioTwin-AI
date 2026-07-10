using Microsoft.Extensions.AI;

namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public interface ILlmChatService
{
    Task<string> CompleteAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        CancellationToken cancellationToken);
}
