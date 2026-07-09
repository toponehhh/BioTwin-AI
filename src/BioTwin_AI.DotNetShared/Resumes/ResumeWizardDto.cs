namespace BioTwin_AI.DotNetShared.Resumes;

public sealed record ResumeWizardDto(
    string Title,
    string Language,
    ResumeWizardProfileDto Profile,
    string Summary,
    IReadOnlyList<ResumeWizardExperienceDto> Experiences,
    IReadOnlyList<ResumeWizardEducationDto> Education,
    IReadOnlyList<string> Skills,
    IReadOnlyList<ResumeWizardProjectDto> Projects,
    IReadOnlyList<string> AdditionalSections);

public sealed record ResumeWizardProfileDto(
    string FullName,
    string Email,
    string Location,
    string? Website);

public sealed record ResumeWizardExperienceDto(
    string Company,
    string Role,
    string StartDate,
    string? EndDate,
    IReadOnlyList<string> Highlights);

public sealed record ResumeWizardEducationDto(
    string Institution,
    string Qualification,
    string StartDate,
    string? EndDate);

public sealed record ResumeWizardProjectDto(
    string Name,
    string Description,
    IReadOnlyList<string> Technologies);
