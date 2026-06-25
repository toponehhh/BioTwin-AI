namespace BioTwin_AI.DotNetShared.Auth;

public sealed record CurrentSessionResponse(
    bool IsAuthenticated,
    int? UserId,
    string? Username,
    string? DisplayName,
    string? Avatar,
    UserRole Role,
    IReadOnlyList<ExternalIdentityProviderDto> ExternalProviders);
