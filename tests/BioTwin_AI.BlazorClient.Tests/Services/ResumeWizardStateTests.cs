using BioTwin_AI.BlazorClient.Services;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.BlazorClient.Tests.Services;

public sealed class ResumeWizardStateTests
{
    [Fact]
    public void Wizard_allows_completed_steps_but_locks_unreached_steps()
    {
        var state = ResumeWizardState.CreateBlank();

        Assert.True(state.CanNavigateTo(ResumeWizardStep.Start));
        Assert.False(state.CanNavigateTo(ResumeWizardStep.Experience));

        state.MarkCompleted(ResumeWizardStep.Start);
        state.MoveNext();

        Assert.Equal(ResumeWizardStep.Profile, state.CurrentStep);
        Assert.True(state.CanNavigateTo(ResumeWizardStep.Start));
        Assert.False(state.CanNavigateTo(ResumeWizardStep.Summary));
    }

    [Fact]
    public void Apply_and_ToDto_preserve_extracted_resume_content()
    {
        var extracted = new ResumeWizardDto(
            "Donald Huang",
            ResumeLanguages.English,
            new ResumeWizardProfileDto("Donald Huang", "donald@example.com", "Shanghai", "https://example.com"),
            "AI engineer",
            [new ResumeWizardExperienceDto("BioTwin", "Engineer", "2025", null, ["Built local AI services"])],
            [new ResumeWizardEducationDto("University", "BSc", "2010", "2014")],
            ["C#", "ONNX"],
            [new ResumeWizardProjectDto("BioTwin AI", "Candidate platform", ["Blazor"])],
            ["Languages: English, Chinese"]);
        var state = ResumeWizardState.CreateBlank();

        state.Apply(extracted);
        var roundTrip = state.ToDto();

        Assert.Equal(extracted.Title, roundTrip.Title);
        Assert.Equal(extracted.Language, roundTrip.Language);
        Assert.Equal(extracted.Profile, roundTrip.Profile);
        Assert.Equal(extracted.Summary, roundTrip.Summary);
        Assert.Single(roundTrip.Experiences);
        Assert.Equal(extracted.Experiences[0].Company, roundTrip.Experiences[0].Company);
        Assert.Equal(extracted.Experiences[0].Role, roundTrip.Experiences[0].Role);
        Assert.Equal(extracted.Experiences[0].StartDate, roundTrip.Experiences[0].StartDate);
        Assert.Equal(extracted.Experiences[0].EndDate, roundTrip.Experiences[0].EndDate);
        Assert.Equal(extracted.Experiences[0].Highlights, roundTrip.Experiences[0].Highlights);
        Assert.Equal(extracted.Education, roundTrip.Education);
        Assert.Equal(extracted.Skills, roundTrip.Skills);
        Assert.Single(roundTrip.Projects);
        Assert.Equal(extracted.Projects[0].Name, roundTrip.Projects[0].Name);
        Assert.Equal(extracted.Projects[0].Description, roundTrip.Projects[0].Description);
        Assert.Equal(extracted.Projects[0].Technologies, roundTrip.Projects[0].Technologies);
        Assert.Equal(extracted.AdditionalSections, roundTrip.AdditionalSections);
        Assert.True(state.IsDirty);
        Assert.Empty(typeof(ResumeWizardState).GetConstructors().Single().GetParameters());
    }
}
