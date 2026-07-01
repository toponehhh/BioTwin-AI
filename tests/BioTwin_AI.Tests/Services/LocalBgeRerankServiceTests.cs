using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class LocalBgeRerankServiceTests
{
    [Fact]
    public async Task RerankAsync_LoadsLocalOnnxModelAndReturnsFiniteScores()
    {
        var contentRoot = GetApplicationContentRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json")
            .Build();

        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.SetupGet(environment => environment.ContentRootPath).Returns(contentRoot);

        using var service = new LocalBgeRerankService(
            environmentMock.Object,
            configuration,
            Mock.Of<ILogger<LocalBgeRerankService>>());

        var results = await service.RerankAsync(
            "csharp project experience",
            ["Built C# and .NET backend services.", "Designed garden layouts."],
            limit: 2);

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.InRange(result.Index, 0, 1);
            Assert.True(double.IsFinite(result.Score), $"Expected finite rerank score, got {result.Score}");
        });
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AppSettingsRerankPathsResolveToLocalBgeRerankModel(string fileName)
    {
        var contentRoot = GetApplicationContentRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile(fileName)
            .Build();

        var modelDirectory = Path.GetFullPath(Path.Combine(contentRoot, configuration["Rerank:ModelDirectory"]!));
        var modelPath = Path.GetFullPath(Path.Combine(modelDirectory, configuration["Rerank:ModelPath"]!));
        var tokenizerPath = Path.GetFullPath(Path.Combine(modelDirectory, configuration["Rerank:TokenizerPath"]!));

        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_rerank_v2", "model.onnx"), modelPath);
        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_rerank_v2", "tokenizer.json"), tokenizerPath);
        Assert.True(File.Exists(modelPath), $"Expected rerank model file to exist: {modelPath}");
        Assert.True(File.Exists(tokenizerPath), $"Expected rerank tokenizer file to exist: {tokenizerPath}");
    }

    private static string GetApplicationContentRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "BioTwin_AI", "BioTwin_AI.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find src/BioTwin_AI/BioTwin_AI.csproj from the test output directory.");
    }
}
