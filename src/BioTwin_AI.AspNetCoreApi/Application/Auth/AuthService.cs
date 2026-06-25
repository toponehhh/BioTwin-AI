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

        if (await dbContext.UserAccounts.AnyAsync(user => user.Username == username, cancellationToken))
        {
            return new AuthResult(false, "Username already exists.", null);
        }

        var user = new UserAccount
        {
            Username = username,
            PasswordHash = HashPassword(request.Password),
            Role = UserRole.Candidate.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
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
            .FirstOrDefaultAsync(candidate => candidate.Username == username, cancellationToken);

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
            user = new UserAccount
            {
                Username = username,
                PasswordHash = HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            Role = UserRole.Interviewer.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
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

    private static string NormalizeUsername(string username)
    {
        return (username ?? string.Empty).Trim().ToLowerInvariant();
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
