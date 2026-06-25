using System.Text.RegularExpressions;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Refinement;

public sealed class ResumeRefinementService : IResumeRefinementService
{
    public RefineMarkdownResponse Refine(RefineMarkdownRequest request)
    {
        var markdown = (request.Markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        markdown = Regex.Replace(markdown, @"[ \t]+$", string.Empty, RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");

        if (!markdown.StartsWith("#", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(request.ResumeTitle))
        {
            markdown = $"# {request.ResumeTitle.Trim()}\n\n{markdown}";
        }

        return new RefineMarkdownResponse(markdown);
    }
}
