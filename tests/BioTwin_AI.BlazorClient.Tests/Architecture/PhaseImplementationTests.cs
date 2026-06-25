namespace BioTwin_AI.BlazorClient.Tests.Architecture;

public sealed class PhaseImplementationTests
{
    [Fact]
    public void Blazor_pages_are_connected_to_api_clients_instead_of_scaffold_text()
    {
        var pagesDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Pages"));

        var pageText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(pagesDirectory, "*.razor", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("scaffold", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionState", pageText, StringComparison.Ordinal);
        Assert.Contains("IChatApiClient", pageText, StringComparison.Ordinal);
        Assert.Contains("IResumeApiClient", pageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_registers_typed_api_clients()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Program.cs"));
        var programText = File.ReadAllText(programPath);

        Assert.Contains("IAuthApiClient", programText, StringComparison.Ordinal);
        Assert.Contains("IChatApiClient", programText, StringComparison.Ordinal);
        Assert.Contains("IResumeApiClient", programText, StringComparison.Ordinal);
        Assert.Contains("ISettingsApiClient", programText, StringComparison.Ordinal);
    }
}
