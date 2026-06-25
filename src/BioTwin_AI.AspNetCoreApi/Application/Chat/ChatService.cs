using System.Runtime.CompilerServices;
using System.Text;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Rag;

namespace BioTwin_AI.AspNetCoreApi.Application.Chat;

public sealed class ChatService(IRagSearchService ragSearchService) : IChatService
{
    public async Task<ChatResponse> AskAsync(string tenantId, bool includeAllTenants, ChatRequest request, CancellationToken cancellationToken)
    {
        var citations = await SearchAsync(tenantId, includeAllTenants, request, cancellationToken);
        var answer = BuildAnswer(request.Question, citations.Results);
        return new ChatResponse(answer, citations.Results);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        string tenantId,
        bool includeAllTenants,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await AskAsync(tenantId, includeAllTenants, request, cancellationToken);
        yield return new ChatStreamChunk(ChatStreamChunkKind.Token, "Searching resume context...\n");

        foreach (var sentence in response.Answer.Split(". ", StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatStreamChunk(ChatStreamChunkKind.Token, sentence.EndsWith('.') ? sentence + " " : sentence + ". ");
            await Task.Delay(20, cancellationToken);
        }

        yield return new ChatStreamChunk(ChatStreamChunkKind.Completed, string.Empty);
    }

    private Task<RagSearchResponse> SearchAsync(string tenantId, bool includeAllTenants, ChatRequest request, CancellationToken cancellationToken)
    {
        return ragSearchService.SearchAsync(
            tenantId,
            includeAllTenants,
            new RagSearchRequest(request.Question, Limit: 3),
            cancellationToken);
    }

    private static string BuildAnswer(string question, IReadOnlyList<RagCitationDto> citations)
    {
        if (citations.Count == 0)
        {
            return "I do not have enough indexed resume context to answer that yet. Upload or save a resume first, then ask again.";
        }

        var builder = new StringBuilder();
        builder.Append("Based on the indexed resume context, ");
        builder.Append("the strongest evidence relates to ");
        builder.Append(string.Join(", ", citations.Select(item => $"{item.ResumeTitle} / {item.SectionTitle}").Distinct()));
        builder.Append(". ");
        builder.Append("For the question \"");
        builder.Append(question.Trim());
        builder.Append("\", the relevant resume notes are: ");
        builder.Append(string.Join(" ", citations.Select(item => item.ContentPreview)));

        return builder.ToString();
    }
}
