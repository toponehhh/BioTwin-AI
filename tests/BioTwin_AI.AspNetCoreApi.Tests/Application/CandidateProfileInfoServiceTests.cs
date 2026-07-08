using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class CandidateProfileInfoServiceTests
{
    [Fact]
    public async Task CreateNextVersionAsync_marks_new_version_current_and_preserves_previous_version()
    {
        await using var context = CreateContext();
        var user = AddUser(context);
        await context.SaveChangesAsync();
        var service = new CandidateProfileInfoService(context);

        var first = await service.CreateNextVersionAsync(
            user.Id,
            "careertimeline",
            """[{"title":"Developer"}]""",
            "llm_generated",
            sourceResumeVersion: 1,
            basedOnInfoId: null,
            createdByUserId: user.Id,
            CancellationToken.None);
        var second = await service.CreateNextVersionAsync(
            user.Id,
            "careertimeline",
            """[{"title":"Senior Developer"}]""",
            "manual_edit",
            sourceResumeVersion: 1,
            basedOnInfoId: first.Id,
            createdByUserId: user.Id,
            CancellationToken.None);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(first.Id, second.BasedOnInfoId);
        Assert.False((await context.CandidateProfileInfos.FindAsync(first.Id))!.IsCurrent);
        Assert.True((await context.CandidateProfileInfos.FindAsync(second.Id))!.IsCurrent);
    }

    [Fact]
    public async Task GetCurrentAsync_returns_current_version_for_info_type()
    {
        await using var context = CreateContext();
        var user = AddUser(context);
        await context.SaveChangesAsync();
        var service = new CandidateProfileInfoService(context);

        await service.CreateNextVersionAsync(user.Id, "worktimeline", """[{"title":"Old"}]""", "llm_generated", null, null, null, CancellationToken.None);
        var expected = await service.CreateNextVersionAsync(user.Id, "worktimeline", """[{"title":"New"}]""", "manual_edit", null, null, user.Id, CancellationToken.None);

        var current = await service.GetCurrentAsync(user.Id, "worktimeline", CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(expected.Id, current.Id);
        Assert.Equal("""[{"title":"New"}]""", current.JsonData);
    }

    private static UserAccount AddUser(BioTwinApiDbContext context)
    {
        var user = new UserAccount
        {
            Username = "candidate",
            Nickname = "Candidate",
            PasswordHash = "hash",
            ProfileHash = "ABCDEFGH"
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
