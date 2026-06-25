using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Chat;
using BioTwin_AI.DotNetShared.Logging;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.DotNetShared.Tests.Contracts;

public class PhaseOneContractTests
{
    [Fact]
    public void CurrentSessionResponse_uses_persisted_role_and_external_provider_contract()
    {
        var provider = new ExternalIdentityProviderDto("GitHub", "GitHub", IsEnabled: false, IsLinked: false);
        var session = new CurrentSessionResponse(
            IsAuthenticated: true,
            UserId: 7,
            Username: "huangd",
            DisplayName: "Huang",
            Avatar: "🧑‍💻",
            Role: UserRole.Admin,
            ExternalProviders: [provider]);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(7, session.UserId);
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
        Assert.DoesNotContain("AvatarEmoji", typeof(RegisterRequest).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("AvatarEmoji", typeof(CurrentSessionResponse).GetProperties().Select(property => property.Name));
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
            SourceFileName: "resume.md",
            SourceContentType: "text/markdown",
            SourceFileSize: 256);

        Assert.Equal("# BioTwin AI", request.Markdown);
        Assert.DoesNotContain(
            typeof(ResumeDetailDto).Assembly.GetTypes().Select(type => type.Name),
            typeName => typeName is "CreateResumeSectionRequest" or "UpdateResumeSectionRequest");
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
