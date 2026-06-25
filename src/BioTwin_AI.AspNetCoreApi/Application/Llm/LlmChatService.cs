using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed class LlmChatService(IChatClient chatClient) : ILlmChatService
{
    public async Task<string> CompleteAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        await foreach (var token in StreamAsync(messages, options, cancellationToken))
        {
            builder.Append(token);
        }

        return builder.ToString().Trim();
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            var emittedContent = false;
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    emittedContent = true;
                    yield return textContent.Text;
                }
            }

            if (!emittedContent && !string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
