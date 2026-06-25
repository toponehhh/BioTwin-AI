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
            Username: null,
            DisplayName: null,
            Role: UserRole.Candidate,
            ExternalProviders: externalProviderCatalog.GetProviders(new HashSet<string>()));
    }

    public async Task<CurrentSessionResponse> CreateAuthenticatedAsync(
        int userId,
        string username,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var linkedProviders = await dbContext.UserExternalIdentities
            .AsNoTracking()
            .Where(identity => identity.UserId == userId)
            .Select(identity => identity.Provider)
            .ToListAsync(cancellationToken);

        return new CurrentSessionResponse(
            IsAuthenticated: true,
            Username: username,
            DisplayName: username,
            Role: role,
            ExternalProviders: externalProviderCatalog.GetProviders(linkedProviders.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}
