using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public async Task ExtractAsync_retries_when_the_model_first_returns_empty_content()
    {
        var llm = new FakeLlmChatService(
            string.Empty,
            """
            {
              "title": "Donald Huang",
              "language": "en",
              "profile": { "fullName": "Donald Huang", "email": "", "location": "", "website": null },
              "summary": "AI engineer",
              "experiences": [],
              "education": [],
              "skills": ["C#"],
              "projects": [],
              "additionalSections": []
            }
            """);
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        var response = await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Donald Huang", ResumeLanguages.English, "Donald Huang"),
            CancellationToken.None);

        Assert.Equal(2, llm.CompletionCount);
        Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
    }

    [Fact]
    public async Task ExtractAsync_retries_when_the_model_first_returns_non_json_content()
    {
        var llm = new FakeLlmChatService(
            "Working on the resume now.",
            ValidResumeJson);
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        var response = await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Donald Huang", ResumeLanguages.English, "Donald Huang"),
            CancellationToken.None);

        Assert.Equal(2, llm.CompletionCount);
        Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
    }

    [Fact]
    public async Task ExtractAsync_retries_when_both_providers_first_return_unusable_content()
    {
        var llm = new FakeLlmChatService(
            new LlmResponseException("Both LLM providers returned unusable content."),
            ValidResumeJson);
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        var response = await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Donald Huang", ResumeLanguages.English, "Donald Huang"),
            CancellationToken.None);

        Assert.Equal(2, llm.CompletionCount);
        Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
    }

    [Fact]
    public async Task ExtractAsync_accepts_json_wrapped_in_model_commentary()
    {
        var llm = new FakeLlmChatService($"Here is the requested JSON:\n{ValidResumeJson}\nDone.");
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        var response = await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Donald Huang", ResumeLanguages.English, "Donald Huang"),
            CancellationToken.None);

        Assert.Equal(1, llm.CompletionCount);
        Assert.Equal("Donald Huang", response.Resume.Profile.FullName);
    }

    [Fact]
    public async Task ExtractAsync_requests_a_strict_resume_json_schema()
    {
        var llm = new FakeLlmChatService("{}");
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, "Resume"),
            CancellationToken.None);

        var responseFormat = Assert.IsType<ChatResponseFormatJson>(
            llm.CompletionOptions.Single().ResponseFormat);
        Assert.True(responseFormat.Schema.HasValue);
        Assert.Equal("resume_wizard", responseFormat.SchemaName);
    }

    [Fact]
    public async Task ExtractAsync_limits_reasoning_to_preserve_tokens_for_the_json_response()
    {
        var llm = new FakeLlmChatService("{}");
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, "Resume"),
            CancellationToken.None);

        var reasoning = Assert.IsType<ReasoningOptions>(llm.CompletionOptions.Single().Reasoning);
        Assert.Equal(ReasoningEffort.Low, reasoning.Effort);
        Assert.Equal(ReasoningOutput.None, reasoning.Output);
    }

    [Fact]
    public async Task ExtractAsync_uses_the_structured_request_kind_and_sufficient_output_budget()
    {
        var llm = new FakeLlmChatService("{}");
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance);

        await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, "Resume"),
            CancellationToken.None);

        var options = Assert.Single(llm.CompletionOptions);
        Assert.Null(options.ModelId);
        Assert.Equal(8000, options.MaxOutputTokens);
        Assert.Equal(LlmRequestKind.StructuredExtraction, Assert.Single(llm.RequestKinds));
    }

    [Fact]
    public async Task ExtractAsync_allows_the_extraction_budget_to_be_configured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LLM:ExtractionMaxTokens"] = "6500"
            })
            .Build();
        var llm = new FakeLlmChatService("{}");
        var service = new ResumeWizardExtractionService(
            llm,
            NullLogger<ResumeWizardExtractionService>.Instance,
            configuration);

        await service.ExtractAsync(
            new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, "Resume"),
            CancellationToken.None);

        var options = Assert.Single(llm.CompletionOptions);
        Assert.Null(options.ModelId);
        Assert.Equal(6500, options.MaxOutputTokens);
    }

    [Fact]
    public async Task ExtractAsync_reports_repeated_empty_content_without_json_parser_noise()
    {
        var service = new ResumeWizardExtractionService(
            new FakeLlmChatService(string.Empty, "  "),
            NullLogger<ResumeWizardExtractionService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsync(
                new ExtractResumeWizardRequest("# Resume", ResumeLanguages.English, "Resume"),
                CancellationToken.None));

        Assert.Equal("Resume extraction returned no content after 2 attempts.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    private sealed class FakeLlmChatService(params object[] responses) : ILlmChatService
    {
        private readonly Queue<object> responses = new(responses);

        public int CompletionCount { get; private set; }
        public List<ChatOptions> CompletionOptions { get; } = [];
        public List<LlmRequestKind> RequestKinds { get; } = [];

        public Task<string> CompleteAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            LlmRequestKind requestKind,
            CancellationToken cancellationToken)
        {
            CompletionCount++;
            CompletionOptions.Add(options);
            RequestKinds.Add(requestKind);
            var response = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
            return response is Exception exception
                ? Task.FromException<string>(exception)
                : Task.FromResult((string)response);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            LlmRequestKind requestKind,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private const string ValidResumeJson = """
    {
      "title": "Donald Huang",
      "language": "en",
      "profile": { "fullName": "Donald Huang", "email": "", "location": "", "website": null },
      "summary": "AI engineer",
      "experiences": [],
      "education": [],
      "skills": ["C#"],
      "projects": [],
      "additionalSections": []
    }
    """;
}
