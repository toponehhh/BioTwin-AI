using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class CandidateProfileExtractionServiceTests
{
    [Fact]
    public async Task GenerateFromResumeAsync_creates_profile_info_versions_and_rotates_profile_hash()
    {
        await using var context = CreateContext();
        var user = new UserAccount
        {
            Username = "candidate",
            Nickname = "Candidate",
            PasswordHash = "hash",
            ProfileHash = "OLDHASH8",
            CandidateProfileVersion = 1
        };
        context.UserAccounts.Add(user);
        await context.SaveChangesAsync();
        var infoService = new CandidateProfileInfoService(context);
        var service = new CandidateProfileExtractionService(context, infoService, new SequenceShareCodeGenerator("NEWHASH8"));

        await service.GenerateFromResumeAsync(
            user.Id,
            "# Candidate\n\n## AI Engineer\nBuilt local AI systems.\n\n## Project Atlas\nDelivered a portfolio timeline.",
            CancellationToken.None);

        var reloadedUser = await context.UserAccounts.SingleAsync();
        Assert.Equal("NEWHASH8", reloadedUser.ProfileHash);
        Assert.Equal(2, reloadedUser.CandidateProfileVersion);
        Assert.Contains(context.CandidateProfileInfos, info => info.InfoType == "careertimeline" && info.Version == 1 && info.IsCurrent);
        Assert.Contains(context.CandidateProfileInfos, info => info.InfoType == "worktimeline" && info.Version == 1 && info.IsCurrent);
    }

    [Fact]
    public async Task GenerateFromResumeAsync_uses_previous_current_versions_as_references()
    {
        await using var context = CreateContext();
        var user = new UserAccount
        {
            Username = "candidate",
            Nickname = "Candidate",
            PasswordHash = "hash",
            ProfileHash = "OLDHASH8",
            CandidateProfileVersion = 1
        };
        context.UserAccounts.Add(user);
        await context.SaveChangesAsync();
        var infoService = new CandidateProfileInfoService(context);
        var first = await infoService.CreateNextVersionAsync(user.Id, "careertimeline", """[{"title":"Old"}]""", "manual_edit", 1, null, user.Id, CancellationToken.None);
        var service = new CandidateProfileExtractionService(context, infoService, new SequenceShareCodeGenerator("NEWHASH8"));

        await service.GenerateFromResumeAsync(user.Id, "## AI Engineer\nBuilt new systems.", CancellationToken.None);

        var latest = await infoService.GetCurrentAsync(user.Id, "careertimeline", CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);
        Assert.Equal(first.Id, latest.BasedOnInfoId);
    }

    private sealed class SequenceShareCodeGenerator(params string[] codes) : IProfileShareCodeGenerator
    {
        private int index;

        public string Generate(int length = ProfileShareCodeGenerator.DefaultLength)
        {
            return codes[Math.Min(index++, codes.Length - 1)];
        }
    }

    private static BioTwinApiDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new BioTwinApiDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
