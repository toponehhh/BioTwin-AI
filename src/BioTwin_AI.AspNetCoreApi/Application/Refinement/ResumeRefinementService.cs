using System.Text.RegularExpressions;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.DotNetShared.Resumes;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace BioTwin_AI.AspNetCoreApi.Application.Refinement;

public sealed class ResumeRefinementService(
    ILlmChatService llmChatService,
    IConfiguration configuration) : IResumeRefinementService
{
    public async Task<RefineMarkdownResponse> RefineAsync(RefineMarkdownRequest request, CancellationToken cancellationToken)
    {
        var markdown = NormalizeMarkdown(request.Markdown, request.ResumeTitle);
        var response = await llmChatService.CompleteAsync(
            BuildMessages(request.ResumeTitle, markdown),
            CreateChatOptions(),
            cancellationToken);

        return string.IsNullOrWhiteSpace(response)
            ? new RefineMarkdownResponse(markdown)
            : new RefineMarkdownResponse(NormalizeMarkdown(response, request.ResumeTitle));
    }

    private static IReadOnlyList<AiChatMessage> BuildMessages(string? resumeTitle, string markdown)
    {
        const string systemPrompt = """
You are a resume Markdown editor.
Rewrite the provided resume Markdown into clean, professional Markdown.
Preserve all factual details and do not invent employers, dates, technologies, awards, or education.
Keep the output as Markdown only, without code fences or commentary.
Use clear headings and concise bullet points where appropriate.
""";

        var userPrompt = $"""
Resume title:
{resumeTitle}

Markdown to refine:
{markdown}
""";

        return
        [
            new AiChatMessage(AiChatRole.System, systemPrompt),
            new AiChatMessage(AiChatRole.User, userPrompt)
        ];
    }

    private AiChatOptions CreateChatOptions()
    {
        return new AiChatOptions
        {
            ModelId = configuration["LLM:Model"] ?? "openrouter/free",
            Temperature = (float)configuration.GetValue("LLM:RefinementTemperature", 0.1),
            MaxOutputTokens = configuration.GetValue("LLM:RefinementMaxTokens", 3000)
        };
    }

    private static string NormalizeMarkdown(string? input, string? resumeTitle)
    {
        var markdown = (input ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        markdown = Regex.Replace(markdown, @"[ \t]+$", string.Empty, RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");

        if (!markdown.StartsWith("#", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(resumeTitle))
        {
            markdown = $"# {resumeTitle.Trim()}\n\n{markdown}";
        }

        return markdown;
    }
}
