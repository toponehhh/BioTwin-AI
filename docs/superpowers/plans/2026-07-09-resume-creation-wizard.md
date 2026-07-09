# Resume Creation Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a seven-step, semi-linear resume creation wizard that supports blank creation and imported-file prefill, writes no draft data, and saves only after final review.

**Architecture:** Shared immutable DTOs describe extracted resume content and a deterministic Markdown builder. A stateless API extraction service uses `ILlmChatService` without database writes. The Blazor client holds mutable wizard state for the current session, renders focused step components, and calls the existing canonical save flow only from Review.

**Tech Stack:** .NET 10, ASP.NET Core controllers, Microsoft.Extensions.AI, System.Text.Json, Blazor WebAssembly, Razor components, xUnit.

## Global Constraints

- Support only `zh-CN` and `en` resume languages.
- Support `Import existing resume` and `Start from scratch` entry paths.
- Use seven steps: Start, Profile, Summary, Experience, Education, Skills & Projects, Review.
- Navigation is semi-linear: completed steps are revisitable; unreached steps are locked; current step must validate before continuing.
- Do not persist wizard drafts or resume progress; refresh or exit discards unsubmitted content after confirmation.
- No `ResumeEntry`, section, vector, candidate-profile, or profile-hash write occurs before final Review confirmation.
- Existing same-language resumes use merge preview before extraction and are updated, not duplicated.
- Full Markdown editing remains in the advanced Resume Workspace after save.
- Use `C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe` for build and test commands.

---

### Task 1: Add Shared Wizard Contracts and Deterministic Markdown Generation

**Files:**
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeWizardDto.cs`
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ExtractResumeWizardRequest.cs`
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ExtractResumeWizardResponse.cs`
- Create: `src/BioTwin_AI.DotNetShared/Resumes/ResumeWizardMarkdownBuilder.cs`
- Modify: `tests/BioTwin_AI.DotNetShared.Tests/Contracts/PhaseOneContractTests.cs`

**Interfaces:**
- Produces: `ResumeWizardDto`, nested resume item records, `ExtractResumeWizardRequest`, `ExtractResumeWizardResponse`, `ResumeWizardMarkdownBuilder.Build(ResumeWizardDto)`.
- Consumes: `ResumeLanguages` constants and normalization rules.

- [ ] **Step 1: Write failing contract and Markdown tests**

```csharp
[Fact]
public void Resume_wizard_contract_builds_deterministic_markdown()
{
    var wizard = new ResumeWizardDto(
        "Donald Huang", ResumeLanguages.English,
        new ResumeWizardProfileDto("Donald Huang", "donald@example.com", "Shanghai", "https://example.com"),
        "Cloud and AI engineer.",
        [new ResumeWizardExperienceDto("BioTwin", "Engineer", "2024-01", null, ["Built local AI systems."])],
        [new ResumeWizardEducationDto("Example University", "Computer Science", "2020", "2024")],
        ["C#", "Azure"],
        [new ResumeWizardProjectDto("BioTwin AI", "Resume intelligence workspace.", ["C#", "Blazor"])],
        []);

    var markdown = ResumeWizardMarkdownBuilder.Build(wizard);

    Assert.Contains("# Donald Huang", markdown, StringComparison.Ordinal);
    Assert.Contains("## Experience", markdown, StringComparison.Ordinal);
    Assert.Contains("### Engineer · BioTwin", markdown, StringComparison.Ordinal);
    Assert.Contains("## Skills", markdown, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test and verify missing types fail compilation**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.DotNetShared.Tests\BioTwin_AI.DotNetShared.Tests.csproj -p:UseAppHost=false --filter "Resume_wizard_contract_builds_deterministic_markdown"
```

Expected: FAIL because wizard contracts do not exist.

- [ ] **Step 3: Add immutable shared records**

```csharp
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

public sealed record ResumeWizardProfileDto(string FullName, string Email, string Location, string? Website);
public sealed record ResumeWizardExperienceDto(string Company, string Role, string StartDate, string? EndDate, IReadOnlyList<string> Highlights);
public sealed record ResumeWizardEducationDto(string Institution, string Qualification, string StartDate, string? EndDate);
public sealed record ResumeWizardProjectDto(string Name, string Description, IReadOnlyList<string> Technologies);
public sealed record ExtractResumeWizardRequest(string Markdown, string Language, string? Title);
public sealed record ExtractResumeWizardResponse(ResumeWizardDto Resume, IReadOnlyList<string> Warnings);
```

- [ ] **Step 4: Implement deterministic Markdown output**

```csharp
public static class ResumeWizardMarkdownBuilder
{
    public static string Build(ResumeWizardDto resume)
    {
        var builder = new StringBuilder().AppendLine($"# {resume.Title.Trim()}").AppendLine();
        AppendProfile(builder, resume.Profile);
        AppendSection(builder, "Summary", resume.Summary);
        AppendExperience(builder, resume.Experiences);
        AppendEducation(builder, resume.Education);
        AppendSkills(builder, resume.Skills);
        AppendProjects(builder, resume.Projects);
        return builder.ToString().Trim() + Environment.NewLine;
    }
}
```

Implement each private append method with stable English headings for `en` and Simplified Chinese headings for `zh-CN`, skip blank optional values, and preserve list order.

- [ ] **Step 5: Run shared tests and commit**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.DotNetShared.Tests\BioTwin_AI.DotNetShared.Tests.csproj -p:UseAppHost=false
git add src/BioTwin_AI.DotNetShared/Resumes tests/BioTwin_AI.DotNetShared.Tests/Contracts/PhaseOneContractTests.cs
git commit -m "feat: add resume wizard contracts"
```

### Task 2: Add Stateless LLM Wizard Extraction

**Files:**
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/IResumeWizardExtractionService.cs`
- Create: `src/BioTwin_AI.AspNetCoreApi/Application/Resumes/ResumeWizardExtractionService.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeWizardExtractionServiceTests.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ResumesController.cs`
- Modify: `src/BioTwin_AI.AspNetCoreApi/Program.cs`

**Interfaces:**
- Consumes: `ILlmChatService.CompleteAsync`, shared extraction request/response, `ResumeLanguages`.
- Produces: `IResumeWizardExtractionService.ExtractAsync(ExtractResumeWizardRequest, CancellationToken)` and `POST /api/resumes/wizard/extract`.

- [ ] **Step 1: Write a failing service test with a fake LLM**

```csharp
[Fact]
public async Task ExtractAsync_returns_structured_content_without_database_dependency()
{
    var llm = new FakeLlmChatService("""{"title":"Donald Huang","language":"en","profile":{"fullName":"Donald Huang","email":"donald@example.com","location":"Shanghai","website":null},"summary":"AI engineer","experiences":[],"education":[],"skills":["C#"],"projects":[],"additionalSections":[]}""");
    var service = new ResumeWizardExtractionService(llm, NullLogger<ResumeWizardExtractionService>.Instance);

    var response = await service.ExtractAsync(new ExtractResumeWizardRequest("# Donald Huang", "en", "Donald Huang"), CancellationToken.None);

    Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
    Assert.Equal(["C#"], response.Resume.Skills);
}
```

- [ ] **Step 2: Run the test and verify missing service types fail compilation**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false --filter "ExtractAsync_returns_structured_content_without_database_dependency"
```

- [ ] **Step 3: Implement the stateless extraction interface and service**

```csharp
public interface IResumeWizardExtractionService
{
    Task<ExtractResumeWizardResponse> ExtractAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken);
}

public sealed class ResumeWizardExtractionService(ILlmChatService llmChatService, ILogger<ResumeWizardExtractionService> logger)
    : IResumeWizardExtractionService
{
    public async Task<ExtractResumeWizardResponse> ExtractAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown)) throw new InvalidOperationException("Resume Markdown cannot be empty.");
        if (!ResumeLanguages.IsSupported(request.Language)) throw new InvalidOperationException("Unsupported resume language.");
        var json = await llmChatService.CompleteAsync(BuildMessages(request), CreateOptions(), cancellationToken);
        var resume = JsonSerializer.Deserialize<ResumeWizardDto>(StripCodeFence(json), JsonOptions)
            ?? throw new InvalidOperationException("Resume extraction returned an empty result.");
        return new ExtractResumeWizardResponse(Normalize(resume, request), []);
    }
}
```

Use a system prompt that requires JSON only, forbids invented facts, preserves dates and ordering, and emits every field in `ResumeWizardDto`. `Normalize` trims strings, normalizes language, converts null collections to empty arrays, and keeps experience in reverse chronological input order.

- [ ] **Step 4: Add the authorized controller endpoint and DI registration**

```csharp
public sealed class ResumesController(IResumeService resumeService, IResumeWizardExtractionService wizardExtractionService) : ControllerBase
{
    [HttpPost("wizard/extract")]
    [Authorize]
    public async Task<ActionResult<ExtractResumeWizardResponse>> ExtractWizard(
        ExtractResumeWizardRequest request,
        CancellationToken cancellationToken) =>
        Ok(await wizardExtractionService.ExtractAsync(request, cancellationToken));
}

builder.Services.AddScoped<IResumeWizardExtractionService, ResumeWizardExtractionService>();
```

- [ ] **Step 5: Add invalid-input and code-fence tests**

Test unsupported language, empty Markdown, fenced JSON, and malformed JSON. Assert the service has no `BioTwinApiDbContext` dependency and no database writes.

- [ ] **Step 6: Run API tests and commit**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false
git add src/BioTwin_AI.AspNetCoreApi tests/BioTwin_AI.AspNetCoreApi.Tests/Application/ResumeWizardExtractionServiceTests.cs
git commit -m "feat: extract resume wizard data"
```

### Task 3: Add Client API and In-Session Wizard State

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Services/ResumeWizardState.cs`
- Create: `tests/BioTwin_AI.BlazorClient.Tests/Services/ResumeWizardStateTests.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/IResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ResumeApiClient.cs`

**Interfaces:**
- Consumes: shared wizard DTOs and `POST api/resumes/wizard/extract`.
- Produces: mutable client state, `ResumeWizardStep`, validation/navigation methods, `ExtractWizardAsync` API method.

- [ ] **Step 1: Write failing semi-linear navigation tests**

```csharp
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
```

- [ ] **Step 2: Implement the mutable state and step enum**

```csharp
public enum ResumeWizardStep { Start, Profile, Summary, Experience, Education, SkillsAndProjects, Review }

public sealed class ResumeWizardState
{
    public ResumeWizardStep CurrentStep { get; private set; }
    public ResumeWizardStep HighestCompletedStep { get; private set; }
    public string CreationMethod { get; set; } = "blank";
    public string Language { get; set; } = ResumeLanguages.SimplifiedChinese;
    public ResumeWizardProfileForm Profile { get; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<ResumeWizardExperienceForm> Experiences { get; } = [];
    public List<ResumeWizardEducationForm> Education { get; } = [];
    public List<string> Skills { get; } = [];
    public List<ResumeWizardProjectForm> Projects { get; } = [];
    public bool IsDirty { get; private set; }
    public bool CanNavigateTo(ResumeWizardStep step) => step <= HighestCompletedStep || step == CurrentStep;
}
```

Add `Apply(ResumeWizardDto)`, `ToDto()`, `MarkDirty()`, `MarkCompleted`, `MoveNext`, `MoveBack`, and step-specific `ValidateCurrentStep()` methods. Do not use browser storage or persistence services.

- [ ] **Step 3: Add the extraction API method**

```csharp
Task<ExtractResumeWizardResponse> ExtractWizardAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken = default);

public Task<ExtractResumeWizardResponse> ExtractWizardAsync(ExtractResumeWizardRequest request, CancellationToken cancellationToken = default) =>
    SendJsonAsync<ExtractResumeWizardResponse>(HttpMethod.Post, "api/resumes/wizard/extract", request, cancellationToken);
```

- [ ] **Step 4: Run client-state tests and commit**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false --filter "ResumeWizardStateTests"
git add src/BioTwin_AI.BlazorClient/Services tests/BioTwin_AI.BlazorClient.Tests/Services/ResumeWizardStateTests.cs
git commit -m "feat: add in-session resume wizard state"
```

### Task 4: Build Focused Wizard Components

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardStepper.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardStartStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardProfileStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardSummaryStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardExperienceStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardEducationStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardSkillsProjectsStep.razor`
- Create: `src/BioTwin_AI.BlazorClient/Components/ResumeWizard/ResumeWizardReviewStep.razor`
- Modify: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: `ResumeWizardState`, `ResumeWizardStep`, `EventCallback` actions.
- Produces: presentational steps with no API or persistence dependencies.

- [ ] **Step 1: Add failing source-contract tests for seven focused components**

```csharp
[Theory]
[InlineData("ResumeWizardStepper.razor")]
[InlineData("ResumeWizardStartStep.razor")]
[InlineData("ResumeWizardProfileStep.razor")]
[InlineData("ResumeWizardSummaryStep.razor")]
[InlineData("ResumeWizardExperienceStep.razor")]
[InlineData("ResumeWizardEducationStep.razor")]
[InlineData("ResumeWizardSkillsProjectsStep.razor")]
[InlineData("ResumeWizardReviewStep.razor")]
public void Resume_wizard_uses_focused_components(string fileName) =>
    Assert.True(File.Exists(GetWizardComponentPath(fileName)));
```

- [ ] **Step 2: Implement the shared responsive stepper**

```razor
<ol class="wizard-stepper" aria-label="Resume creation progress">
@foreach (var item in Steps)
{
    <li class="@GetClass(item.Step)">
        <button type="button" disabled="@(!State.CanNavigateTo(item.Step))" @onclick="() => StepSelected.InvokeAsync(item.Step)">
            <span class="wizard-step-marker">@(IsCompleted(item.Step) ? "✓" : item.Number)</span>
            <span class="wizard-step-label">@item.Label</span>
        </button>
    </li>
}
</ol>
<div class="wizard-mobile-progress"><span>@CurrentLabel</span><strong>@($"{CurrentNumber} / 7")</strong><progress max="7" value="@CurrentNumber"></progress></div>
```

- [ ] **Step 3: Implement Start, Profile, and Summary components**

Start exposes creation method, language, and one `InputFile`; Profile exposes full name, email, location, and website; Summary exposes professional title and summary textarea. Each component accepts `[Parameter, EditorRequired] public ResumeWizardState State { get; set; }` and calls `State.MarkDirty()` on input.

- [ ] **Step 4: Implement repeatable Experience and Education components**

```razor
@foreach (var experience in State.Experiences)
{
    <fieldset class="wizard-repeatable-group">
        <input aria-label="Company" @bind="experience.Company" />
        <input aria-label="Role" @bind="experience.Role" />
        <input aria-label="Start date" @bind="experience.StartDate" />
        <input aria-label="End date" @bind="experience.EndDate" />
        <textarea aria-label="Highlights" @bind="experience.HighlightsText"></textarea>
    </fieldset>
}
<button type="button" class="command-button secondary" @onclick="AddExperience">+ Add another experience</button>
```

Education follows the same repeatable pattern with institution, qualification, start, and end dates.

- [ ] **Step 5: Implement Skills & Projects and read-only Review**

Skills use removable text tokens; projects use repeatable name, description, and technology fields. Review calls `ResumeWizardMarkdownBuilder.Build(State.ToDto())`, renders the Markdown in a preformatted preview, and emits `EditStepRequested` callbacks for each section. It does not contain `MarkdownEditor`.

- [ ] **Step 6: Run component contract tests and commit**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false --filter "Resume_wizard_uses_focused_components"
git add src/BioTwin_AI.BlazorClient/Components/ResumeWizard tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "feat: add resume wizard steps"
```

### Task 5: Orchestrate Import, Merge, Extraction, and Final Save

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Pages/ResumeCreate.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Layout/MainLayout.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeUpload.razor`
- Modify: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: `IResumeApiClient`, `ResumeWizardState`, all step components, `NavigationManager`, `SessionState`.
- Produces: `/resume/create`, import prefill, same-language merge-before-extract, final save, unsaved navigation lock.

- [ ] **Step 1: Add failing route and behavior contract tests**

```csharp
[InlineData("ResumeCreate.razor", "@page \"/resume/create\"")]

Assert.Contains("<NavigationLock", pageText, StringComparison.Ordinal);
Assert.Contains("ResumeWizardStepper", pageText, StringComparison.Ordinal);
Assert.Contains("ExtractWizardAsync", pageText, StringComparison.Ordinal);
Assert.Contains("MergePreviewAsync", pageText, StringComparison.Ordinal);
Assert.Contains("SaveResumeAsync", pageText, StringComparison.Ordinal);
Assert.DoesNotContain("localStorage", pageText, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Implement page shell and step rendering**

```razor
@page "/resume/create"
@inject IResumeApiClient ResumeApiClient
@inject NavigationManager Navigation

<ProtectedArea Title="Resume creation requires sign in" Description="Sign in to create or import a resume.">
    <NavigationLock ConfirmExternalNavigation="@state.IsDirty" OnBeforeInternalNavigation="ConfirmInternalNavigation" />
    <section class="resume-wizard-page">
        <header class="workspace-header"><p class="eyebrow">Resume Builder</p><h1>Create your resume</h1></header>
        <div class="resume-wizard-panel">
            <ResumeWizardStepper State="state" StepSelected="NavigateToStep" />
            @switch (state.CurrentStep) { @* render exactly one focused step component *@ }
            <footer class="wizard-actions">@* Cancel, Back, Continue or Save resume *@</footer>
        </div>
    </section>
</ProtectedArea>
```

- [ ] **Step 3: Implement import, merge, and extraction orchestration**

```csharp
private async Task ImportAsync(InputFileChangeEventArgs args)
{
    var file = args.File;
    await using var stream = file.OpenReadStream(10 * 1024 * 1024);
    var converted = await ResumeApiClient.ConvertUploadAsync(file.Name, file.ContentType, stream);
    var sourceMarkdown = converted.Markdown;
    var existing = summaries.FirstOrDefault(item => item.Language == converted.DetectedLanguage);
    if (existing is not null)
    {
        var merged = await ResumeApiClient.MergePreviewAsync(new(converted.DetectedLanguage, converted.Title, sourceMarkdown, file.Name));
        sourceMarkdown = merged.MergedMarkdown;
    }
    var extracted = await ResumeApiClient.ExtractWizardAsync(new(sourceMarkdown, converted.DetectedLanguage, converted.Title));
    state.Apply(extracted.Resume);
}
```

Load summaries after authentication so same-language imports are detected before extraction. Keep converted Markdown in a private page field for retry; extraction failure does not navigate away or write data.

- [ ] **Step 4: Implement semi-linear navigation and final save**

```csharp
private async Task ContinueAsync()
{
    errors = state.ValidateCurrentStep();
    if (errors.Count > 0) return;
    state.MarkCompleted(state.CurrentStep);
    state.MoveNext();
}

private async Task SaveAsync()
{
    var dto = state.ToDto();
    var markdown = ResumeWizardMarkdownBuilder.Build(dto);
    await ResumeApiClient.SaveResumeAsync(new(dto.Title, markdown, dto.Language, sourceFileName, sourceContentType, sourceFileSize, sourceFileBase64));
    state.MarkSaved();
    Navigation.NavigateTo("/resume/workspace");
}
```

- [ ] **Step 5: Add Admin navigation and redirect legacy upload entry**

Add `/resume/create` as `Create Resume` in the Admin menu. Change `ResumeUpload.razor` into a protected route redirect to `/resume/create` so old bookmarks continue to work.

- [ ] **Step 6: Run Blazor tests and commit orchestration**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
git add src/BioTwin_AI.BlazorClient tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "feat: orchestrate resume creation wizard"
```

### Task 6: Match the Approved Obsidian Glass Wizard Preview

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`
- Reference: `docs/superpowers/specs/assets/2026-07-09-resume-creation-wizard-preview.png`

**Interfaces:**
- Consumes: shell tokens from the unified-site-shell plan and wizard classes from Tasks 4-5.
- Produces: responsive horizontal/mobile progress, balanced fields, stable action bar, and 8px glass panel.

- [ ] **Step 1: Add failing CSS contract assertions**

```csharp
Assert.Contains(".resume-wizard-panel", appCss, StringComparison.Ordinal);
Assert.Contains(".wizard-stepper", appCss, StringComparison.Ordinal);
Assert.Contains(".wizard-mobile-progress", appCss, StringComparison.Ordinal);
Assert.Contains(".wizard-actions", appCss, StringComparison.Ordinal);
Assert.Contains("position: sticky", appCss, StringComparison.Ordinal);
```

- [ ] **Step 2: Implement desktop wizard layout**

```css
.resume-wizard-page { max-width: 1200px; margin-inline: auto; }
.resume-wizard-panel { overflow: hidden; border: 1px solid var(--border); border-radius: var(--panel-radius); background: var(--glass-bg); backdrop-filter: blur(var(--glass-blur)); }
.wizard-stepper { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); margin: 0; padding: 1.5rem 1.75rem; list-style: none; }
.wizard-step-marker { display: grid; width: 2.25rem; height: 2.25rem; place-items: center; border: 1px solid var(--border); border-radius: 50%; }
.wizard-step.is-active .wizard-step-marker { color: #11132d; background: var(--accent); box-shadow: 0 0 24px color-mix(in srgb, var(--accent) 35%, transparent); }
.wizard-step-body { padding: 1.75rem; border-top: 1px solid var(--border); }
.wizard-actions { position: sticky; bottom: 0; display: flex; align-items: center; justify-content: space-between; padding: 1rem 1.75rem; border-top: 1px solid var(--border); background: color-mix(in srgb, var(--surface-strong) 88%, transparent); }
```

- [ ] **Step 3: Implement repeatable form and mobile progress behavior**

```css
.wizard-form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem 1.25rem; }
.wizard-repeatable-group { margin: 0; padding: 1.25rem 0; border: 0; border-bottom: 1px solid var(--border); }
.wizard-mobile-progress { display: none; }
@media (max-width: 760px) { .wizard-stepper { display: none; } .wizard-mobile-progress { display: grid; gap: .5rem; padding: 1rem; } .wizard-form-grid { grid-template-columns: 1fr; } .wizard-actions { padding: .875rem 1rem; } }
```

- [ ] **Step 4: Run client tests and build**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build src\BioTwin_AI.BlazorClient\BioTwin_AI.BlazorClient.csproj -p:UseAppHost=false
```

- [ ] **Step 5: Commit wizard styling**

```powershell
git add src/BioTwin_AI.BlazorClient/wwwroot/css/app.css tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "style: match resume wizard preview"
```

### Task 7: End-to-End Verification

**Files:**
- Verify all files from Tasks 1-6.

**Interfaces:**
- Consumes: complete wizard and existing canonical save pipeline.
- Produces: proof that no pre-review writes occur and final save retains existing behavior.

- [ ] **Step 1: Run all solution tests**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test BioTwin_AI.slnx -p:UseAppHost=false --logger "console;verbosity=minimal"
```

Expected: all test projects pass.

- [ ] **Step 2: Verify blank English and Chinese creation manually**

Complete all seven steps, navigate backward to edit a completed step, confirm unreached steps remain locked, save, and verify one canonical resume with sections and vectors for each language.

- [ ] **Step 3: Verify import and same-language update manually**

Import a supported file, confirm fields are prefilled, import a second file for the same language, confirm merge occurs before extraction, save, and verify the existing canonical record is updated rather than duplicated.

- [ ] **Step 4: Verify no-draft and failure behavior**

Start the wizard and leave before Review; confirm the warning and no database write. Force extraction and final-save failures; confirm retry is possible and current in-memory state remains visible.

- [ ] **Step 5: Capture desktop and mobile screenshots**

Compare `/resume/create` at approximately `1440x900` and `390x844` with the approved preview. Verify Header alignment, seven-step desktop progress, compact mobile progress, fixed action hierarchy, readable controls, and no overlap.

- [ ] **Step 6: Commit evidence-driven corrections**

```powershell
git add src tests
git commit -m "fix: polish resume creation flow"
```

