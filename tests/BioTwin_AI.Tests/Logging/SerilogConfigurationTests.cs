using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace BioTwin_AI.Tests.Logging;

public class SerilogConfigurationTests
{
    [Fact]
    public void Program_UsesSerilogHostIntegration()
    {
        var programText = File.ReadAllText(GetRepoPath("src", "BioTwin_AI", "Program.cs"));

        Assert.Contains("using Serilog;", programText);
        Assert.Contains("builder.Host.UseSerilog", programText);
        Assert.DoesNotContain("builder.Logging.AddConsole()", programText);
    }

    [Fact]
    public void Project_ReferencesSerilogPackages()
    {
        var project = XDocument.Load(GetRepoPath("src", "BioTwin_AI", "BioTwin_AI.csproj"));
        var packageNames = project
            .Descendants("PackageReference")
            .Select(package => package.Attribute("Include")?.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Serilog.AspNetCore", packageNames);
        Assert.Contains("Serilog.Settings.Configuration", packageNames);
        Assert.Contains("Serilog.Sinks.File", packageNames);
    }

    [Fact]
    public void AppSettings_ConfiguresConsoleAndRollingFileSinks()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetRepoPath("src", "BioTwin_AI", "appsettings.json")));
        var serilog = document.RootElement.GetProperty("Serilog");
        var overrides = serilog.GetProperty("MinimumLevel").GetProperty("Override");
        var writeTo = serilog.GetProperty("WriteTo").EnumerateArray().ToList();

        Assert.Equal("Information", overrides.GetProperty("Microsoft.Hosting.Lifetime").GetString());
        Assert.Contains(writeTo, sink => sink.GetProperty("Name").GetString() == "Console");

        var fileSink = Assert.Single(writeTo, sink => sink.GetProperty("Name").GetString() == "File");
        var args = fileSink.GetProperty("Args");
        Assert.Equal("logs/biotwin-.log", args.GetProperty("path").GetString());
        Assert.Equal("Day", args.GetProperty("rollingInterval").GetString());
        Assert.Equal(14, args.GetProperty("retainedFileCountLimit").GetInt32());
        Assert.True(args.GetProperty("shared").GetBoolean());
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
