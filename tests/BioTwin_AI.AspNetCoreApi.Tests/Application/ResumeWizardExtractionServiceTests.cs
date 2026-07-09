using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeWizardExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_returns_structured_content_from_fenced_json()
    {
        var llm = new FakeLlmChatService(
            """
            ```json
            {
              "title": "Donald Huang",
              "language": "en",
              "profile": {
                "fullName": "Donald Huang",
                "email": "donald@example.com",
                "location": "Shanghai",
                "website": null
              },
              "summary": "AI engineer",
              "experiences": [],
              "education": [],
              "skills": ["C#"],
              "projects": [],
              "additionalSections": []
            }
            ```
            """);
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        var response = await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Donald Huang", ResumeLanguages.English, "Donald Huang"),
            CancellationToken.None);

        Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
        Assert.Equal(ResumeLanguages.English, response.Resume.Language);
        Assert.Equal(["C#"], response.Resume.Skills);
        Assert.Empty(response.Warnings);
    }

    [Theory]
    [InlineData("", ResumeLanguages.English, "Resume Markdown cannot be empty.")]
    [InlineData("# Resume", "fr", "Unsupported resume language 'fr'.")]
    public async Task ExtractAsync_rejects_invalid_input(
        string markdown,
        string language,
        string expectedMessage)
    {
        var service = new ResumeWizardExtractionService(
            new FakeLlmChatService("{}"),
            NullLogger<ResumeWizardExtractionService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync(
                new ExtractResumeWizardRequest(markdown, language, null),
                CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_reports_malformed_json_without_database_writes()
    {
        var service = new ResumeWizardExtractionService(
            new FakeLlmChatService("not-json"),
            NullLogger<ResumeWizardExtractionService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync(
                new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, null),
                CancellationToken.None));

        Assert.Equal("Resume extraction returned invalid JSON.", exception.Message);
        Assert.DoesNotContain(
            typeof(ResumeWizardExtractionService).GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType.Name.Contains("DbContext", StringComparison.Ordinal));
    }

    private sealed class FakeLlmChatService(string response) : ILlmChatService
    {
        public Task<string> CompleteAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
