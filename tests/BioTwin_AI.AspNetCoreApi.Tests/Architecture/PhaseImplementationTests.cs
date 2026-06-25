namespace BioTwin_AI.AspNetCoreApi.Tests.Architecture;

public sealed class PhaseImplementationTests
{
    [Fact]
    public void Controllers_do_not_return_not_implemented_scaffold_responses()
    {
        var controllerDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Controllers"));

        var controllerText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(controllerDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Status501NotImplemented", controllerText, StringComparison.Ordinal);
        Assert.DoesNotContain("501", controllerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_program_registers_milestone_one_infrastructure()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Program.cs"));
        var programText = File.ReadAllText(programPath);

        Assert.Contains("AddDbContext", programText, StringComparison.Ordinal);
        Assert.Contains("AddCors", programText, StringComparison.Ordinal);
        Assert.Contains("SetIsOriginAllowed", programText, StringComparison.Ordinal);
        Assert.Contains("IsLocalDevelopmentOrigin", programText, StringComparison.Ordinal);
        Assert.Contains("UseSerilog", programText, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatedAsync", programText, StringComparison.Ordinal);
        Assert.Contains("UseAuthentication", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_configuration_declares_local_database_and_development_all2md()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));
        var developmentText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.Development.json"));

        Assert.Contains("\"BioTwinApi\": \"Data Source=database/biotwin-api.db\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"ApiUrl\": \"http://localhost:8000\"", developmentText, StringComparison.Ordinal);
    }
}
