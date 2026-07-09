using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeWizardExtractionService
{
    Task<ExtractResumeWizardResponse> ExtractAsync(
        ExtractResumeWizardRequest request,
        CancellationToken cancellationToken);
}
