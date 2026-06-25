using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public interface IExternalProviderCatalog
{
    IReadOnlyList<ExternalIdentityProviderDto> GetProviders(IReadOnlySet<string> linkedProviders);
}
