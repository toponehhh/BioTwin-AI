namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ExtractResumeWizardRequest(
    string Markdown,
    string Language,
    string? Title);
