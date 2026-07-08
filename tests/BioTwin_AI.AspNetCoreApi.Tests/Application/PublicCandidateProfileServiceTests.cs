using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class PublicCandidateProfileServiceTests
{
    [Fact]
    public async Task GetAsync_without_uid_returns_default_candidate_profile()
    {
        await using var context = CreateContext();
        var user = AddCandidate(context, "default", "K7X4Q9MF", isDefault: true);
        await context.SaveChangesAsync();
        var infoService = new CandidateProfileInfoService(context);
        await infoService.CreateNextVersionAsync(
            user.Id,
            "careertimeline",
            """[{"title":"AI Engineer","subtitle":"BioTwin","periodLabel":"2026","sortOrder":0,"description":"Built profile intelligence.","isHighlighted":true}]""",
            "llm_generated",
            null,
            null,
            user.Id,
            CancellationToken.None);

        var service = new PublicCandidateProfileService(context);

        var profile = await service.GetAsync(null, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("Default", profile.Candidate.DisplayName);
        Assert.Single(profile.CareerTimelineItems);
        Assert.Equal("AI Engineer", profile.CareerTimelineItems[0].Title);
    }

    [Fact]
    public async Task GetAsync_with_uid_normalizes_code_and_rejects_private_profiles()
    {
        await using var context = CreateContext();
        var publicUser = AddCandidate(context, "public", "K7X4Q9MF", isDefault: false);
        _ = AddCandidate(context, "private", "ABCDEFGH", isDefault: false, isPublic: false);
        await context.SaveChangesAsync();
        var infoService = new CandidateProfileInfoService(context);
        await infoService.CreateNextVersionAsync(publicUser.Id, "worktimeline", """[{"number":"01","title":"Portfolio","description":"Built a public profile.","imageUrl":null,"linkUrl":null,"sortOrder":0}]""", "manual_edit", null, null, publicUser.Id, CancellationToken.None);

        var service = new PublicCandidateProfileService(context);

        var publicProfile = await service.GetAsync(" k7x4-q9mf ", CancellationToken.None);
        var privateProfile = await service.GetAsync("abcdefgh", CancellationToken.None);

        Assert.NotNull(publicProfile);
        Assert.Equal("Public", publicProfile.Candidate.DisplayName);
        Assert.Single(publicProfile.WorkTimelineItems);
        Assert.Null(privateProfile);
    }

    private static UserAccount AddCandidate(
        BioTwinApiDbContext context,
        string username,
        string profileHash,
        bool isDefault,
        bool isPublic = true)
    {
        var user = new UserAccount
        {
            Username = username,
            Nickname = char.ToUpperInvariant(username[0]) + username[1..],
            PasswordHash = "hash",
            Role = UserRole.Candidate.ToString(),
            ProfileHash = profileHash,
            IsDefaultCandidate = isDefault,
            IsProfilePublic = isPublic,
            Roles = [new UserRoleAssignment { Role = UserRole.Candidate.ToString() }]
        };
        context.UserAccounts.Add(user);
        return user;
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
