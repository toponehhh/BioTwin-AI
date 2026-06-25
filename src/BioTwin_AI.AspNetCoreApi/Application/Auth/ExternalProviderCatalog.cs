using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public sealed class ExternalProviderCatalog : IExternalProviderCatalog
{
    private static readonly ExternalIdentityProviderDto[] Providers =
    [
        new("GitHub", "GitHub", IsEnabled: false, IsLinked: false),
        new("Google", "Google", IsEnabled: false, IsLinked: false),
        new("Microsoft", "Microsoft", IsEnabled: false, IsLinked: false),
        new("CloudflareAccess", "Cloudflare Access", IsEnabled: false, IsLinked: false)
    ];

    public IReadOnlyList<ExternalIdentityProviderDto> GetProviders(IReadOnlySet<string> linkedProviders)
    {
        return Providers
            .Select(provider => provider with { IsLinked = linkedProviders.Contains(provider.Provider) })
            .ToArray();
    }
}
