namespace BioTwin_AI.DotNetShared.Auth;

public sealed record ExternalIdentityProviderDto(
    string Provider,
    string DisplayName,
    bool IsEnabled,
    bool IsLinked);
