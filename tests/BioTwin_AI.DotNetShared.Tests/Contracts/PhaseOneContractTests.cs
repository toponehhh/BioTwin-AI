using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Chat;
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
            Username: "huangd",
            DisplayName: "Huang",
            Role: UserRole.Admin,
            ExternalProviders: [provider]);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(UserRole.Admin, session.Role);
        Assert.Single(session.ExternalProviders);
        Assert.Equal("GitHub", session.ExternalProviders[0].Provider);
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
}
