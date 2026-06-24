using System.Text.Json;
using Xunit;

namespace BioTwin_AI.Tests.Dependencies;

public class DependencyVersionTests
{
    [Fact]
    public void RestoredAssets_DoNotUseVulnerableSqlitePclRawNativePackage()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetRepoPath("src", "BioTwin_AI", "obj", "project.assets.json")));
        var libraries = document.RootElement.GetProperty("libraries");

        Assert.False(
            libraries.TryGetProperty("SQLitePCLRaw.lib.e_sqlite3/2.1.11", out _),
            "SQLitePCLRaw.lib.e_sqlite3 2.1.11 has GHSA-2m69-gcr7-jv3q and should not be restored.");
    }

    private static string GetRepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BioTwin_AI.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
