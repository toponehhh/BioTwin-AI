using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public sealed class UserRoleService(BioTwinApiDbContext dbContext) : IUserRoleService
{
    public async Task<IReadOnlyList<UserRole>> GetRolesAsync(int userId, CancellationToken cancellationToken)
    {
        var assignedRoles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(role => role.UserId == userId)
            .OrderBy(role => role.Id)
            .Select(role => role.Role)
            .ToListAsync(cancellationToken);

        if (assignedRoles.Count > 0)
        {
            return assignedRoles.Select(ParseRole).Distinct().ToArray();
        }

        var legacyRole = await dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return [ParseRole(legacyRole)];
    }

    public async Task EnsureRoleAsync(int userId, UserRole role, CancellationToken cancellationToken)
    {
        var roleName = role.ToString();
        var exists = await dbContext.UserRoles
            .AnyAsync(assignment => assignment.UserId == userId && assignment.Role == roleName, cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.UserRoles.Add(new UserRoleAssignment
        {
            UserId = userId,
            Role = roleName,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static UserRole ParseRole(string? role)
    {
        return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : UserRole.Candidate;
    }
}
