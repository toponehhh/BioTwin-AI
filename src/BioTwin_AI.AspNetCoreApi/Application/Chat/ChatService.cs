using System.Runtime.CompilerServices;
using System.Text;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Rag;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace BioTwin_AI.AspNetCoreApi.Application.Chat;

public sealed class ChatService(
    IRagSearchService ragSearchService,
    ILlmChatService llmChatService,
    IConfiguration configuration) : IChatService
{
    public async Task<ChatResponse> AskAsync(string tenantId, bool includeAllTenants, ChatRequest request, CancellationToken cancellationToken)
    {
        var citations = await SearchAsync(tenantId, includeAllTenants, request, cancellationToken);
        var answer = await llmChatService.CompleteAsync(
            BuildMessages(includeAllTenants, request.Question, citations.Results),
            CreateChatOptions(),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = "The language model returned an empty response. Try again or add more resume context.";
        }

        return new ChatResponse(answer, citations.Results);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        string tenantId,
        bool includeAllTenants,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var citations = await SearchAsync(tenantId, includeAllTenants, request, cancellationToken);
        yield return new ChatStreamChunk(ChatStreamChunkKind.Token, "Searching resume context...\n");

        await foreach (var token in llmChatService.StreamAsync(
            BuildMessages(includeAllTenants, request.Question, citations.Results),
            CreateChatOptions(),
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatStreamChunk(ChatStreamChunkKind.Token, token);
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

    private static IReadOnlyList<AiChatMessage> BuildMessages(
        bool includeAllTenants,
        string question,
        IReadOnlyList<RagCitationDto> citations)
    {
        var systemPrompt = includeAllTenants
            ? """
You are an interviewer's assistant for BioTwin AI.
Answer objectively from the supplied resume context.
When comparing candidates or resumes, name the resume title and section used as evidence.
If the context is insufficient, say so clearly and do not invent experience.
"""
            : """
You are a candidate interview assistant for BioTwin AI.
Answer in first person when the question is about the user's experience.
Use only the supplied resume context for factual claims.
If the context is insufficient, say that the indexed resume data does not contain enough evidence.
""";

        return
        [
            new AiChatMessage(AiChatRole.System, systemPrompt),
            new AiChatMessage(AiChatRole.User, BuildUserPrompt(question, citations))
        ];
    }

    private static string BuildUserPrompt(string question, IReadOnlyList<RagCitationDto> citations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Question:");
        builder.AppendLine(question.Trim());
        builder.AppendLine();
        builder.AppendLine("Indexed resume context:");

        if (citations.Count == 0)
        {
            builder.AppendLine("No matching resume sections were found.");
            return builder.ToString();
        }

        foreach (var citation in citations)
        {
            builder.AppendLine($"- Resume: {citation.ResumeTitle}");
            builder.AppendLine($"  Section: {citation.SectionTitle}");
            builder.AppendLine($"  Relevance: {citation.Score:P0}");
            builder.AppendLine($"  Content: {citation.ContentPreview}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private AiChatOptions CreateChatOptions()
    {
        return new AiChatOptions
        {
            ModelId = configuration["LLM:Model"] ?? "openrouter/free",
            Temperature = (float)configuration.GetValue("LLM:Temperature", 0.2),
            MaxOutputTokens = configuration.GetValue("LLM:MaxTokens", 800)
        };
    }
}
