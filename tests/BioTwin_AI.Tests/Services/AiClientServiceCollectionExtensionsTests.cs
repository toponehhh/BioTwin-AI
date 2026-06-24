using BioTwin_AI.Services;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class AiClientServiceCollectionExtensionsTests
{
    [Fact]
    public void GetApiKey_BlankConfiguredKey_UsesEnvironmentKey()
    {
        const string envVarName = "OPENROUTER_API_KEY";
        const string expectedApiKey = "test-openrouter-key";
        var previousValue = Environment.GetEnvironmentVariable(envVarName);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, expectedApiKey);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "LLM:ApiKey", "" }
                })
                .Build();

            var apiKey = InvokeGetApiKey(config);

            Assert.Equal(expectedApiKey, apiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, previousValue);
        }
    }

    private static string InvokeGetApiKey(IConfiguration configuration)
    {
        var method = typeof(AiClientServiceCollectionExtensions).GetMethod(
            "GetApiKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, new object[] { configuration }));
    }
}
