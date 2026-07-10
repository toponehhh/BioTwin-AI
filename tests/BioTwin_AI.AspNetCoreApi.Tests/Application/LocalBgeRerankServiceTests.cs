using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class LocalBgeRerankServiceTests
{
    [Fact]
    public async Task RerankAsync_loads_local_model_and_returns_finite_scores()
    {
        var solutionRoot = FindSolutionRoot();
        var projectRoot = Path.Combine(solutionRoot, "src", "BioTwin_AI.AspNetCoreApi");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rerank:ModelDirectory"] = Path.Combine(solutionRoot, "LLM", "bge_rerank_v2"),
                ["Rerank:ModelPath"] = "model.onnx",
                ["Rerank:TokenizerPath"] = "tokenizer.json",
                ["Rerank:MaxTokens"] = "512"
            })
            .Build();
        var environment = new FakeHostEnvironment(projectRoot);

        using var service = new LocalBgeRerankService(
            environment,
            configuration,
            NullLogger<LocalBgeRerankService>.Instance);
        var results = await service.RerankAsync(
            "C# backend experience",
            ["Built .NET APIs.", "Designed garden layouts."],
            2);

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.InRange(result.Index, 0, 1);
            Assert.True(double.IsFinite(result.Score));
        });
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BioTwin_AI.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BioTwin_AI.slnx was not found.");
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "BioTwin_AI.AspNetCoreApi.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
