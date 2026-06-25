using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface IAuthApiClient
{
    Task<CurrentSessionResponse> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> InterviewerLoginAsync(CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalIdentityProviderDto>> GetExternalProvidersAsync(CancellationToken cancellationToken = default);
}
