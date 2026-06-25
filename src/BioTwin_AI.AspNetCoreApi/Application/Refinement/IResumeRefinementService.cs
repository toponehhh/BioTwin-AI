using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Refinement;

public interface IResumeRefinementService
{
    Task<RefineMarkdownResponse> RefineAsync(RefineMarkdownRequest request, CancellationToken cancellationToken);
}
