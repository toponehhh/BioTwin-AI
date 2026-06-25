using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResult> InterviewerLoginAsync(CancellationToken cancellationToken);

    Task<AuthResult> UpdateProfileAsync(string username, UpdateProfileRequest request, CancellationToken cancellationToken);
}
