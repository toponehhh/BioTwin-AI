using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.BlazorClient.Services.Api;

namespace BioTwin_AI.BlazorClient.Services;

public sealed class SessionState(IAuthApiClient authApiClient)
{
    public CurrentSessionResponse? Current { get; private set; }

    public bool IsAuthenticated => Current?.IsAuthenticated == true;

    public string DisplayName => Current?.DisplayName ?? Current?.Username ?? "Anonymous";

    public string Avatar => Current?.Avatar ?? "🧑‍💻";

    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Current = await authApiClient.GetCurrentSessionAsync(cancellationToken);
        }
        catch
        {
            Current = new CurrentSessionResponse(false, null, null, null, null, UserRole.Candidate, []);
        }

        Changed?.Invoke();
    }

    public async Task<AuthResult> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var result = await authApiClient.UpdateProfileAsync(request, cancellationToken);
        if (result.Success)
        {
            await RefreshAsync(cancellationToken);
        }

        return result;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await authApiClient.LogoutAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }
}
