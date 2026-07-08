namespace BioTwin_AI.DotNetShared.Auth;

public sealed record CurrentSessionResponse(
    bool IsAuthenticated,
    int? UserId,
    string? Username,
    string? DisplayName,
    string? Avatar,
    IReadOnlyList<UserRole> Roles,
    IReadOnlyList<ExternalIdentityProviderDto> ExternalProviders)
{
    public UserRole Role => Roles.Contains(UserRole.Admin)
        ? UserRole.Admin
        : Roles.Contains(UserRole.Interviewer)
            ? UserRole.Interviewer
            : UserRole.Candidate;
}
