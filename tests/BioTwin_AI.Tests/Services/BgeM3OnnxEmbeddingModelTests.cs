using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Reflection;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class BgeM3OnnxEmbeddingModelTests
{
    [Fact]
    public void DefaultEmbeddingModelPathsResolveToMovedBgeM3Directory()
    {
        var contentRoot = GetApplicationContentRoot();
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.SetupGet(environment => environment.ContentRootPath).Returns(contentRoot);

        var configuration = new ConfigurationBuilder().Build();

        var modelDirectory = InvokeResolveModelDirectory(environmentMock.Object, configuration);
        var modelPath = InvokeResolveModelFile(modelDirectory, null, "bge_m3_model.onnx");
        var tokenizerPath = InvokeResolveModelFile(modelDirectory, null, "bge_m3_tokenizer.onnx");

        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_m3"), modelDirectory);
        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_m3", "bge_m3_model.onnx"), modelPath);
        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_m3", "bge_m3_tokenizer.onnx"), tokenizerPath);
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AppSettingsEmbeddingPathsResolveToMovedBgeM3Directory(string fileName)
    {
        var contentRoot = GetApplicationContentRoot();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile(fileName)
            .Build();

        var modelDirectory = Path.GetFullPath(Path.Combine(contentRoot, configuration["Embedding:ModelDirectory"]!));
        var modelPath = Path.GetFullPath(Path.Combine(modelDirectory, configuration["Embedding:ModelPath"]!));
        var tokenizerPath = Path.GetFullPath(Path.Combine(modelDirectory, configuration["Embedding:TokenizerPath"]!));

        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_m3", "bge_m3_model.onnx"), modelPath);
        Assert.Equal(Path.Combine(contentRoot, "LLM", "bge_m3", "bge_m3_tokenizer.onnx"), tokenizerPath);
        Assert.True(File.Exists(modelPath), $"Expected model file to exist: {modelPath}");
        Assert.True(File.Exists(tokenizerPath), $"Expected tokenizer file to exist: {tokenizerPath}");
    }

    private static string InvokeResolveModelDirectory(IHostEnvironment environment, IConfiguration configuration)
    {
        var method = typeof(BgeM3OnnxEmbeddingModel).GetMethod(
            "ResolveModelDirectory",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, new object[] { environment, configuration }));
    }

    private static string InvokeResolveModelFile(string modelDirectory, string? configuredPath, string defaultFileName)
    {
        var method = typeof(BgeM3OnnxEmbeddingModel).GetMethod(
            "ResolveModelFile",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, new object?[] { modelDirectory, configuredPath, defaultFileName }));
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
