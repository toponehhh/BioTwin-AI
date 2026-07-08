using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Logging;
using BioTwin_AI.DotNetShared.Profiles;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.DotNetShared.Tests.Contracts;

public class PhaseOneContractTests
{
    [Fact]
    public void CurrentSessionResponse_uses_persisted_roles_and_external_provider_contract()
    {
        var provider = new ExternalIdentityProviderDto("GitHub", "GitHub", IsEnabled: false, IsLinked: false);
        var session = new CurrentSessionResponse(
            IsAuthenticated: true,
            UserId: 7,
            Username: "huangd",
            DisplayName: "Huang",
            Avatar: "🧑‍💻",
            Roles: [UserRole.Candidate, UserRole.Admin],
            ExternalProviders: [provider]);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(7, session.UserId);
        Assert.Equal([UserRole.Candidate, UserRole.Admin], session.Roles);
        Assert.Equal(UserRole.Admin, session.Role);
        Assert.Equal("🧑‍💻", session.Avatar);
        Assert.Single(session.ExternalProviders);
        Assert.Equal("GitHub", session.ExternalProviders[0].Provider);
    }

    [Fact]
    public void Auth_contract_requires_nickname_avatar_and_profile_update_payload()
    {
        var authTypes = typeof(RegisterRequest).Assembly.GetTypes().Select(type => type.Name).ToHashSet();

        Assert.Contains("UpdateProfileRequest", authTypes);
        Assert.Contains("Nickname", typeof(RegisterRequest).GetProperties().Select(property => property.Name));
        Assert.Contains("Avatar", typeof(RegisterRequest).GetProperties().Select(property => property.Name));
        Assert.Contains("Avatar", typeof(CurrentSessionResponse).GetProperties().Select(property => property.Name));
        Assert.Contains("Roles", typeof(CurrentSessionResponse).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("AvatarEmoji", typeof(RegisterRequest).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("AvatarEmoji", typeof(CurrentSessionResponse).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void Candidate_profile_contract_carries_template_sections_and_info_versions()
    {
        var profile = new CandidateProfileDto(
            Candidate: new CandidateProfileCandidateDto(
                DisplayName: "Huang",
                Headline: ".NET AI Engineer",
                Avatar: "🧑‍💻",
                Summary: "Builds AI systems.",
                ProfileHashUpdatedAt: DateTimeOffset.Parse("2026-07-07T00:00:00Z"),
                CandidateProfileVersion: 3),
            CareerTimelineItems:
            [
                new CareerTimelineItemDto(
                    Title: "AI Engineer",
                    Subtitle: "BioTwin AI",
                    PeriodLabel: "2026",
                    SortOrder: 0,
                    Description: "Built candidate profile intelligence.",
                    IsHighlighted: true)
            ],
            WorkTimelineItems:
            [
                new WorkTimelineItemDto(
                    Number: "01",
                    Title: "Profile Template",
                    Description: "Rendered public candidate data.",
                    ImageUrl: null,
                    LinkUrl: null,
                    SortOrder: 0)
            ],
            Skills: ["C#", "Blazor"],
            Education: [],
            Certifications: [],
            PublicContact: null,
            InfoVersions:
            [
                new CandidateProfileInfoVersionDto(
                    InfoType: "careertimeline",
                    Version: 3,
                    Source: "manual_edit",
                    UpdatedAt: DateTimeOffset.Parse("2026-07-07T00:00:00Z"))
            ]);

        Assert.Equal("Huang", profile.Candidate.DisplayName);
        Assert.Single(profile.CareerTimelineItems);
        Assert.Single(profile.WorkTimelineItems);
        Assert.Contains(profile.InfoVersions, info => info.InfoType == "careertimeline" && info.Version == 3);
    }

    [Fact]
    public void ChatStreamChunk_supports_ndjson_streaming_chunks()
    {
        var chunk = new ChatStreamChunk(ChatStreamChunkKind.Token, "hello");

        Assert.Equal(ChatStreamChunkKind.Token, chunk.Kind);
        Assert.Equal("hello", chunk.Content);
    }

    [Fact]
    public void Resume_contract_keeps_full_markdown_save_as_the_only_edit_request()
    {
        var request = new SaveResumeMarkdownRequest(
            Title: "Cloudflare Migration Resume",
            Markdown: "# BioTwin AI",
            Language: ResumeLanguages.English,
            SourceFileName: "resume.md",
            SourceContentType: "text/markdown",
            SourceFileSize: 256);

        Assert.Equal("# BioTwin AI", request.Markdown);
        Assert.Equal(ResumeLanguages.English, request.Language);
        Assert.DoesNotContain(
            typeof(ResumeDetailDto).Assembly.GetTypes().Select(type => type.Name),
            typeName => typeName is "CreateResumeSectionRequest" or "UpdateResumeSectionRequest");
    }

    [Fact]
    public void Resume_contract_supports_language_slots_and_merge_preview()
    {
        var converted = new ConvertedResumeFileDto(
            Title: "Donald Huang",
            SourceFileName: "donald.md",
            Markdown: "# Donald Huang",
            DetectedLanguage: ResumeLanguages.SimplifiedChinese,
            IsDuplicate: false,
            ExistingResumeEntryId: null,
            ExistingResumeTitle: null);
        var summary = new ResumeSummaryDto(
            Id: 1,
            Title: "Donald Huang",
            Language: ResumeLanguages.SimplifiedChinese,
            SourceFileName: "donald.md",
            CreatedAt: DateTimeOffset.Parse("2026-07-08T00:00:00Z"),
            SectionCount: 2,
            HasOriginalFile: true);
        var detail = new ResumeDetailDto(
            Id: 1,
            Title: "Donald Huang",
            Language: ResumeLanguages.SimplifiedChinese,
            SourceFileName: "donald.md",
            CreatedAt: DateTimeOffset.Parse("2026-07-08T00:00:00Z"),
            Sections: []);
        var request = new MergeResumeMarkdownRequest(
            Language: ResumeLanguages.SimplifiedChinese,
            DraftTitle: "Donald Huang new",
            DraftMarkdown: "# Donald Huang\n\n## 2026",
            SourceFileName: "donald-new.md");
        var response = new MergeResumeMarkdownResponse(
            Language: ResumeLanguages.SimplifiedChinese,
            CanonicalResumeId: 1,
            MergedTitle: "Donald Huang",
            MergedMarkdown: "# Donald Huang\n\n## 2026",
            Warnings: []);

        Assert.Equal(ResumeLanguages.SimplifiedChinese, converted.DetectedLanguage);
        Assert.Equal(ResumeLanguages.SimplifiedChinese, summary.Language);
        Assert.Equal(ResumeLanguages.SimplifiedChinese, detail.Language);
        Assert.Equal("donald-new.md", request.SourceFileName);
        Assert.Equal(1, response.CanonicalResumeId);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public void Client_log_contract_carries_browser_log_details_to_the_api()
    {
        var request = new ClientLogEntryRequest(
            Level: "Information",
            Category: "BioTwin_AI.BlazorClient.Startup",
            Message: "Client started",
            Exception: null,
            Url: "http://localhost:5193/",
            Timestamp: DateTimeOffset.UtcNow);

        Assert.Equal("Information", request.Level);
        Assert.Equal("BioTwin_AI.BlazorClient.Startup", request.Category);
        Assert.Equal("Client started", request.Message);
        Assert.Equal("http://localhost:5193/", request.Url);
    }
}
