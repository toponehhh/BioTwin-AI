namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ExtractResumeWizardResponse(
    ResumeWizardDto Resume,
    IReadOnlyList<string> Warnings);
