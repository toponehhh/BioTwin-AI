using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ApiAuthServiceTests
{
    [Fact]
    public async Task RegisterAsync_creates_short_profile_hash_and_candidate_role_assignment()
    {
        await using var context = CreateContext();
        var roleService = new UserRoleService(context);
        var sessionFactory = new SessionResponseFactory(context, new ExternalProviderCatalog(), roleService);
        var authService = new AuthService(context, sessionFactory, roleService, new StubShareCodeGenerator("K7X4Q9MF"));

        var result = await authService.RegisterAsync(
            new RegisterRequest("candidate", "password123", "Candidate", "C"),
            CancellationToken.None);

        var user = await context.UserAccounts.SingleAsync();
        Assert.True(result.Success);
        Assert.Equal("K7X4Q9MF", user.ProfileHash);
        Assert.Equal([UserRole.Candidate], result.Session!.Roles);
        Assert.Contains(context.UserRoles, role => role.UserId == user.Id && role.Role == UserRole.Candidate.ToString());
    }

    [Fact]
    public async Task CreateAuthenticatedAsync_returns_anonymous_when_cookie_user_no_longer_exists()
    {
        await using var context = CreateContext();
        var roleService = new UserRoleService(context);
        var sessionFactory = new SessionResponseFactory(context, new ExternalProviderCatalog(), roleService);

        var session = await sessionFactory.CreateAuthenticatedAsync(
            userId: 404,
            username: "deleted-user",
            role: UserRole.Admin,
            CancellationToken.None);

        Assert.False(session.IsAuthenticated);
        Assert.Null(session.UserId);
        Assert.Null(session.Username);
        Assert.Equal([UserRole.Candidate], session.Roles);
    }

    private sealed class StubShareCodeGenerator(string code) : BioTwin_AI.AspNetCoreApi.Application.Profiles.IProfileShareCodeGenerator
    {
        public string Generate(int length = BioTwin_AI.AspNetCoreApi.Application.Profiles.ProfileShareCodeGenerator.DefaultLength)
        {
            return code;
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
