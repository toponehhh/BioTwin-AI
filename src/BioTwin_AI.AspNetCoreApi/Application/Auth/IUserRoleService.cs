using BioTwin_AI.DotNetShared.Auth;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public interface IUserRoleService
{
    Task<IReadOnlyList<UserRole>> GetRolesAsync(int userId, CancellationToken cancellationToken);

    Task EnsureRoleAsync(int userId, UserRole role, CancellationToken cancellationToken);
}
