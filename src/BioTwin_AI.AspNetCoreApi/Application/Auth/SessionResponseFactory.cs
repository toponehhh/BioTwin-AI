using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public sealed class SessionResponseFactory(
    BioTwinApiDbContext dbContext,
    IExternalProviderCatalog externalProviderCatalog,
    IUserRoleService userRoleService) : ISessionResponseFactory
{
    public CurrentSessionResponse CreateAnonymous()
    {
        return new CurrentSessionResponse(
            IsAuthenticated: false,
            UserId: null,
            Username: null,
            DisplayName: null,
            Avatar: null,
            Roles: [UserRole.Candidate],
            ExternalProviders: externalProviderCatalog.GetProviders(new HashSet<string>()));
    }

    public async Task<CurrentSessionResponse> CreateAuthenticatedAsync(
        int userId,
        string username,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var user = userId > 0
            ? await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(account => account.Id == userId, cancellationToken)
            : await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(account => account.Username == username, cancellationToken);

        if (user is null)
        {
            return CreateAnonymous();
        }

        var resolvedUserId = user.Id;
        var resolvedUsername = user.Username;
        var displayName = string.IsNullOrWhiteSpace(user?.Nickname) ? resolvedUsername : user.Nickname;
        var avatar = string.IsNullOrWhiteSpace(user?.Avatar) ? "🧑‍💻" : user.Avatar;

        var linkedProviders = await dbContext.UserExternalIdentities
            .AsNoTracking()
            .Where(identity => identity.UserId == resolvedUserId)
            .Select(identity => identity.Provider)
            .ToListAsync(cancellationToken);

        var roles = resolvedUserId > 0
            ? await userRoleService.GetRolesAsync(resolvedUserId, cancellationToken)
            : [role];

        return new CurrentSessionResponse(
            IsAuthenticated: true,
            UserId: resolvedUserId > 0 ? resolvedUserId : null,
            Username: resolvedUsername,
            DisplayName: displayName,
            Avatar: avatar,
            Roles: roles.Count > 0 ? roles : [role],
            ExternalProviders: externalProviderCatalog.GetProviders(linkedProviders.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}
