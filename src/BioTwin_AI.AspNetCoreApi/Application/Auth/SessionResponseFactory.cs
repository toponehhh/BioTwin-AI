using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public sealed class SessionResponseFactory(
    BioTwinApiDbContext dbContext,
    IExternalProviderCatalog externalProviderCatalog) : ISessionResponseFactory
{
    public CurrentSessionResponse CreateAnonymous()
    {
        return new CurrentSessionResponse(
            IsAuthenticated: false,
            UserId: null,
            Username: null,
            DisplayName: null,
            Avatar: null,
            Role: UserRole.Candidate,
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

        var resolvedUserId = user?.Id ?? userId;
        var resolvedUsername = user?.Username ?? username;
        var displayName = string.IsNullOrWhiteSpace(user?.Nickname) ? resolvedUsername : user.Nickname;
        var avatar = string.IsNullOrWhiteSpace(user?.Avatar) ? "🧑‍💻" : user.Avatar;

        var linkedProviders = await dbContext.UserExternalIdentities
            .AsNoTracking()
            .Where(identity => identity.UserId == resolvedUserId)
            .Select(identity => identity.Provider)
            .ToListAsync(cancellationToken);

        return new CurrentSessionResponse(
            IsAuthenticated: true,
            UserId: resolvedUserId > 0 ? resolvedUserId : null,
            Username: resolvedUsername,
            DisplayName: displayName,
            Avatar: avatar,
            Role: role,
            ExternalProviders: externalProviderCatalog.GetProviders(linkedProviders.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}
