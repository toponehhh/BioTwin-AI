using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.BlazorClient.Services;

public enum ResumeWizardStep
{
    Start,
    Profile,
    Summary,
    Experience,
    Education,
    SkillsAndProjects,
    Review
}

public sealed class ResumeWizardState
{
    private int highestCompletedStep = -1;

    public ResumeWizardStep CurrentStep { get; private set; } = ResumeWizardStep.Start;

    public ResumeWizardStep? HighestCompletedStep => highestCompletedStep >= 0
        ? (ResumeWizardStep)highestCompletedStep
        : null;

    public string CreationMethod { get; set; } = "blank";

    public string Language { get; set; } = ResumeLanguages.SimplifiedChinese;

    public string Title { get; set; } = string.Empty;

    public ResumeWizardProfileForm Profile { get; } = new();

    public string Summary { get; set; } = string.Empty;

    public List<ResumeWizardExperienceForm> Experiences { get; } = [];

    public List<ResumeWizardEducationForm> Education { get; } = [];

    public List<string> Skills { get; } = [];

    public List<ResumeWizardProjectForm> Projects { get; } = [];

    public List<string> AdditionalSections { get; } = [];

    public bool IsDirty { get; private set; }

    public static ResumeWizardState CreateBlank() => new();

    public bool IsCompleted(ResumeWizardStep step) => (int)step <= highestCompletedStep;

    public bool CanNavigateTo(ResumeWizardStep step) => step == CurrentStep || IsCompleted(step);

    public bool NavigateTo(ResumeWizardStep step)
    {
        if (!CanNavigateTo(step))
        {
            return false;
        }

        CurrentStep = step;
        return true;
    }

    public void MarkCompleted(ResumeWizardStep step)
    {
        highestCompletedStep = Math.Max(highestCompletedStep, (int)step);
    }

    public bool MoveNext()
    {
        if (CurrentStep == ResumeWizardStep.Review || !IsCompleted(CurrentStep))
        {
            return false;
        }

        CurrentStep++;
        return true;
    }

    public bool MoveBack()
    {
        if (CurrentStep == ResumeWizardStep.Start)
        {
            return false;
        }

        CurrentStep--;
        return true;
    }

    public IReadOnlyList<string> ValidateCurrentStep()
    {
        var errors = new List<string>();
        switch (CurrentStep)
        {
            case ResumeWizardStep.Start:
                if (CreationMethod is not ("blank" or "import"))
                {
                    errors.Add("Choose how to create the resume.");
                }

                if (!ResumeLanguages.IsSupported(Language))
                {
                    errors.Add("Choose a supported resume language.");
                }

                break;
            case ResumeWizardStep.Profile:
                if (string.IsNullOrWhiteSpace(Profile.FullName))
                {
                    errors.Add("Full name is required.");
                }

                break;
            case ResumeWizardStep.Summary:
                if (string.IsNullOrWhiteSpace(Title))
                {
                    errors.Add("Resume title is required.");
                }

                break;
            case ResumeWizardStep.Experience:
                if (Experiences.Any(item => item.HasContent &&
                    (string.IsNullOrWhiteSpace(item.Company) || string.IsNullOrWhiteSpace(item.Role))))
                {
                    errors.Add("Each experience needs a company and role.");
                }

                break;
            case ResumeWizardStep.Education:
                if (Education.Any(item => item.HasContent &&
                    (string.IsNullOrWhiteSpace(item.Institution) || string.IsNullOrWhiteSpace(item.Qualification))))
                {
                    errors.Add("Each education entry needs an institution and qualification.");
                }

                break;
        }

        return errors;
    }

    public void MarkDirty() => IsDirty = true;

    public void MarkSaved() => IsDirty = false;

    public void Apply(ResumeWizardDto resume)
    {
        Title = resume.Title;
        Language = ResumeLanguages.Normalize(resume.Language);
        Profile.FullName = resume.Profile.FullName;
        Profile.Email = resume.Profile.Email;
        Profile.Location = resume.Profile.Location;
        Profile.Website = resume.Profile.Website ?? string.Empty;
        Summary = resume.Summary;

        Experiences.Clear();
        Experiences.AddRange(resume.Experiences.Select(ResumeWizardExperienceForm.FromDto));
        Education.Clear();
        Education.AddRange(resume.Education.Select(ResumeWizardEducationForm.FromDto));
        Skills.Clear();
        Skills.AddRange(resume.Skills);
        Projects.Clear();
        Projects.AddRange(resume.Projects.Select(ResumeWizardProjectForm.FromDto));
        AdditionalSections.Clear();
        AdditionalSections.AddRange(resume.AdditionalSections);
        MarkDirty();
    }

    public ResumeWizardDto ToDto() => new(
        Clean(Title),
        ResumeLanguages.Normalize(Language),
        new ResumeWizardProfileDto(
            Clean(Profile.FullName),
            Clean(Profile.Email),
            Clean(Profile.Location),
            Optional(Profile.Website)),
        Clean(Summary),
        Experiences.Where(item => item.HasContent).Select(item => item.ToDto()).ToArray(),
        Education.Where(item => item.HasContent).Select(item => item.ToDto()).ToArray(),
        Skills.Where(item => !string.IsNullOrWhiteSpace(item)).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Projects.Where(item => item.HasContent).Select(item => item.ToDto()).ToArray(),
        AdditionalSections.Where(item => !string.IsNullOrWhiteSpace(item)).Select(Clean).ToArray());

    internal static string Clean(string? value) => value?.Trim() ?? string.Empty;

    internal static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
}

public sealed class ResumeWizardProfileForm
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
}

public sealed class ResumeWizardExperienceForm
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string HighlightsText { get; set; } = string.Empty;

    public bool HasContent => !string.IsNullOrWhiteSpace(Company) ||
        !string.IsNullOrWhiteSpace(Role) ||
        !string.IsNullOrWhiteSpace(StartDate) ||
        !string.IsNullOrWhiteSpace(EndDate) ||
        !string.IsNullOrWhiteSpace(HighlightsText);

    public static ResumeWizardExperienceForm FromDto(ResumeWizardExperienceDto item) => new()
    {
        Company = item.Company,
        Role = item.Role,
        StartDate = item.StartDate,
        EndDate = item.EndDate ?? string.Empty,
        HighlightsText = string.Join(Environment.NewLine, item.Highlights)
    };

    public ResumeWizardExperienceDto ToDto() => new(
        ResumeWizardState.Clean(Company),
        ResumeWizardState.Clean(Role),
        ResumeWizardState.Clean(StartDate),
        ResumeWizardState.Optional(EndDate),
        ResumeWizardState.SplitLines(HighlightsText));
}

public sealed class ResumeWizardEducationForm
{
    public string Institution { get; set; } = string.Empty;
    public string Qualification { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;

    public bool HasContent => !string.IsNullOrWhiteSpace(Institution) ||
        !string.IsNullOrWhiteSpace(Qualification) ||
        !string.IsNullOrWhiteSpace(StartDate) ||
        !string.IsNullOrWhiteSpace(EndDate);

    public static ResumeWizardEducationForm FromDto(ResumeWizardEducationDto item) => new()
    {
        Institution = item.Institution,
        Qualification = item.Qualification,
        StartDate = item.StartDate,
        EndDate = item.EndDate ?? string.Empty
    };

    public ResumeWizardEducationDto ToDto() => new(
        ResumeWizardState.Clean(Institution),
        ResumeWizardState.Clean(Qualification),
        ResumeWizardState.Clean(StartDate),
        ResumeWizardState.Optional(EndDate));
}

public sealed class ResumeWizardProjectForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TechnologiesText { get; set; } = string.Empty;

    public bool HasContent => !string.IsNullOrWhiteSpace(Name) ||
        !string.IsNullOrWhiteSpace(Description) ||
        !string.IsNullOrWhiteSpace(TechnologiesText);

    public static ResumeWizardProjectForm FromDto(ResumeWizardProjectDto item) => new()
    {
        Name = item.Name,
        Description = item.Description,
        TechnologiesText = string.Join(", ", item.Technologies)
    };

    public ResumeWizardProjectDto ToDto() => new(
        ResumeWizardState.Clean(Name),
        ResumeWizardState.Clean(Description),
        TechnologiesText
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ResumeWizardState.Clean)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
}
