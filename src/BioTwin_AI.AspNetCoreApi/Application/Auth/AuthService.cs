using System.Security.Cryptography;
using System.Text;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Auth;

public sealed class AuthService(
    BioTwinApiDbContext dbContext,
    ISessionResponseFactory sessionResponseFactory) : IAuthService
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = NormalizeUsername(request.Username);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new AuthResult(false, "Username and password are required.", null);
        }

        if (await dbContext.UserAccounts.IgnoreQueryFilters().AnyAsync(user => user.Username == username, cancellationToken))
        {
            return new AuthResult(false, "Username already exists.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Username = username,
            Nickname = NormalizeNickname(request.Nickname, username),
            Avatar = NormalizeAvatar(request.Avatar),
            PasswordHash = HashPassword(request.Password),
            Role = UserRole.Candidate.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.UserAccounts.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var session = await sessionResponseFactory.CreateAuthenticatedAsync(
            user.Id,
            user.Username,
            ParseRole(user.Role),
            cancellationToken);

        return new AuthResult(true, "Registered and signed in successfully.", session);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = NormalizeUsername(request.Username);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new AuthResult(false, "Username and password are required.", null);
        }

        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(candidate => candidate.Username == username && !candidate.IsDeleted, cancellationToken);

        if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            return new AuthResult(false, "Invalid username or password.", null);
        }

        var session = await sessionResponseFactory.CreateAuthenticatedAsync(
            user.Id,
            user.Username,
            ParseRole(user.Role),
            cancellationToken);

        return new AuthResult(true, "Logged in successfully.", session);
    }

    public async Task<AuthResult> InterviewerLoginAsync(CancellationToken cancellationToken)
    {
        const string username = "interviewer";

        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(candidate => candidate.Username == username, cancellationToken);

        if (user is null)
        {
            var now = DateTimeOffset.UtcNow;
            user = new UserAccount
            {
                Username = username,
                Nickname = "Interviewer",
                Avatar = "🕵️",
                PasswordHash = HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                Role = UserRole.Interviewer.ToString(),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.UserAccounts.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var session = await sessionResponseFactory.CreateAuthenticatedAsync(
            user.Id,
            user.Username,
            ParseRole(user.Role),
            cancellationToken);

        return new AuthResult(true, "Interviewer session started.", session);
    }

    public async Task<AuthResult> UpdateProfileAsync(
        string username,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = NormalizeUsername(username);
        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(candidate => candidate.Username == normalizedUsername, cancellationToken);

        if (user is null)
        {
            return new AuthResult(false, "Current user was not found.", null);
        }

        user.Nickname = NormalizeNickname(request.Nickname, user.Username);
        user.Avatar = NormalizeAvatar(request.Avatar);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var session = await sessionResponseFactory.CreateAuthenticatedAsync(
            user.Id,
            user.Username,
            ParseRole(user.Role),
            cancellationToken);

        return new AuthResult(true, "Profile updated.", session);
    }

    private static string NormalizeUsername(string username)
    {
        return (username ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeNickname(string? nickname, string username)
    {
        var normalized = (nickname ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? username : normalized;
    }

    private static string NormalizeAvatar(string? avatar)
    {
        var normalized = (avatar ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "🧑‍💻" : normalized;
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static UserRole ParseRole(string role)
    {
        return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : UserRole.Candidate;
    }
}
