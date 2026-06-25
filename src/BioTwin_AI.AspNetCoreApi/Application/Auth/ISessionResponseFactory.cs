using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public interface ISessionResponseFactory
{
    CurrentSessionResponse CreateAnonymous();

    Task<CurrentSessionResponse> CreateAuthenticatedAsync(
        int userId,
        string username,
        UserRole role,
        CancellationToken cancellationToken);
}
