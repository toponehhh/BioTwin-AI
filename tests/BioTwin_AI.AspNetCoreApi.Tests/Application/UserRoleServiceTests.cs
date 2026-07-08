using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class UserRoleServiceTests
{
    [Fact]
    public async Task GetRolesAsync_returns_assignments_or_legacy_role_when_no_assignments_exist()
    {
        await using var context = CreateContext();
        var assignedUser = new UserAccount
        {
            Username = "assigned",
            Nickname = "Assigned",
            PasswordHash = "hash",
            Role = UserRole.Candidate.ToString(),
            ProfileHash = "ABCDEFGH",
            Roles =
            [
                new UserRoleAssignment { Role = UserRole.Candidate.ToString() },
                new UserRoleAssignment { Role = UserRole.Admin.ToString() }
            ]
        };
        var legacyUser = new UserAccount
        {
            Username = "legacy",
            Nickname = "Legacy",
            PasswordHash = "hash",
            Role = UserRole.Interviewer.ToString(),
            ProfileHash = "JKLMNPQR"
        };
        context.UserAccounts.AddRange(assignedUser, legacyUser);
        await context.SaveChangesAsync();

        var service = new UserRoleService(context);

        Assert.Equal([UserRole.Candidate, UserRole.Admin], await service.GetRolesAsync(assignedUser.Id, CancellationToken.None));
        Assert.Equal([UserRole.Interviewer], await service.GetRolesAsync(legacyUser.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRoleAsync_adds_each_role_only_once()
    {
        await using var context = CreateContext();
        var user = new UserAccount
        {
            Username = "candidate",
            Nickname = "Candidate",
            PasswordHash = "hash",
            Role = UserRole.Candidate.ToString(),
            ProfileHash = "ABCDEFGH"
        };
        context.UserAccounts.Add(user);
        await context.SaveChangesAsync();

        var service = new UserRoleService(context);

        await service.EnsureRoleAsync(user.Id, UserRole.Candidate, CancellationToken.None);
        await service.EnsureRoleAsync(user.Id, UserRole.Candidate, CancellationToken.None);

        Assert.Single(context.UserRoles);
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
