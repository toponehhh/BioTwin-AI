using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class AuthApiClient(HttpClient httpClient) : ApiClientBase(httpClient), IAuthApiClient
{
    public Task<CurrentSessionResponse> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<CurrentSessionResponse>("api/session/current", cancellationToken);
    }

    public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<AuthResult>(HttpMethod.Post, "api/auth/register", request, cancellationToken);
    }

    public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<AuthResult>(HttpMethod.Post, "api/auth/login", request, cancellationToken);
    }

    public Task<AuthResult> InterviewerLoginAsync(CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<AuthResult>(HttpMethod.Post, "api/auth/interviewer-login", null, cancellationToken);
    }

    public Task<AuthResult> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        return SendJsonAsync<AuthResult>(HttpMethod.Put, "api/auth/profile", request, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        return SendJsonAsync(HttpMethod.Post, "api/auth/logout", null, cancellationToken);
    }

    public Task<IReadOnlyList<ExternalIdentityProviderDto>> GetExternalProvidersAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<ExternalIdentityProviderDto>>("api/auth/external/providers", cancellationToken);
    }
}
