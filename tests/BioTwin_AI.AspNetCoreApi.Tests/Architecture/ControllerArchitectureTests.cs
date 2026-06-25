using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Tests.Architecture;

public class ControllerArchitectureTests
{
    [Fact]
    public void Api_uses_dedicated_controllers_for_phase_one_feature_areas()
    {
        var controllerNames = GetControllerNames();

        string[] expectedControllers =
        [
            "AuthController",
            "SessionController",
            "ResumesController",
            "ResumeExportController",
            "ResumeRefinementController",
            "ChatController",
            "RagController",
            "ClientLogsController",
            "HealthController"
        ];

        foreach (var expectedController in expectedControllers)
        {
            Assert.Contains(expectedController, controllerNames);
        }

        Assert.DoesNotContain("ResumeSectionsController", controllerNames);
        Assert.DoesNotContain("WeatherForecastController", controllerNames);
    }

    [Fact]
    public void Api_program_does_not_define_business_minimal_api_routes()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Program.cs"));
        var programText = File.ReadAllText(programPath);

        Assert.DoesNotContain("MapGet(", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost(", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPut(", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("MapDelete(", programText, StringComparison.Ordinal);
        Assert.Contains("MapControllers()", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_program_uses_local_development_friendly_host_configuration()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Program.cs"));
        var programText = File.ReadAllText(programPath);

        Assert.Contains("builder.Logging.ClearProviders()", programText, StringComparison.Ordinal);
        Assert.Contains("builder.Host.UseSerilog", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Logging.AddConsole()", programText, StringComparison.Ordinal);
        Assert.Contains("!app.Environment.IsDevelopment()", programText, StringComparison.Ordinal);
        Assert.Contains("app.UseHttpsRedirection()", programText, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetControllerNames()
    {
        var apiAssembly = Assembly.Load("BioTwin_AI.AspNetCoreApi");

        return apiAssembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true })
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
