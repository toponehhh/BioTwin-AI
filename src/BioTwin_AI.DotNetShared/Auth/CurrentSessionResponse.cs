namespace BioTwin_AI.DotNetShared.Auth;

public sealed record CurrentSessionResponse(
    bool IsAuthenticated,
    string? Username,
    string? DisplayName,
    UserRole Role,
    IReadOnlyList<ExternalIdentityProviderDto> ExternalProviders);
