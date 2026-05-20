using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.Security.Cryptography;
using System.Text;

namespace BioTwin_AI.Services
{
    public class AuthService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly CurrentUserSession _session;
        private readonly IStringLocalizer<SharedResource>? _localizer;

        public AuthService(BioTwinDbContext dbContext, CurrentUserSession session)
            : this(dbContext, session, null)
        {
        }

        public AuthService(
            BioTwinDbContext dbContext,
            CurrentUserSession session,
            IStringLocalizer<SharedResource>? localizer)
        {
            _dbContext = dbContext;
            _session = session;
            _localizer = localizer;
        }

        public async Task<(bool Success, string Message)> RegisterAsync(string username, string password)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, T("Username and password are required."));
            }

            var exists = await UsernameExistsAsync(username);
            if (exists)
            {
                return (false, T("Username already exists."));
            }

            var user = new UserAccount
            {
                Username = username,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.UserAccounts.Add(user);
            await _dbContext.SaveChangesAsync();
            _session.SignIn(user.Username, UserRole.Candidate, CreateSessionToken(user, UserRole.Candidate));

            return (true, T("Registered and signed in successfully."));
        }

        public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, T("Username and password are required."));
            }

            var user = await FindUserByNormalizedUsernameAsync(username);
            if (user == null)
            {
                return (false, T("Invalid username or password."));
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                return (false, T("Invalid username or password."));
            }

            _session.SignIn(user.Username, UserRole.Candidate, CreateSessionToken(user, UserRole.Candidate));
            return (true, T("Logged in successfully."));
        }

        public async Task<bool> RestoreSessionAsync(IJSRuntime jsRuntime)
        {
            await _session.RestoreAsync(jsRuntime);
            if (!_session.IsAuthenticated || string.IsNullOrWhiteSpace(_session.Username))
            {
                return false;
            }

            if (_session.IsInterviewer)
            {
                return true;
            }

            var username = NormalizeUsername(_session.Username);
            if (string.IsNullOrWhiteSpace(_session.SessionToken))
            {
                await RejectPersistedSessionAsync(jsRuntime);
                return false;
            }

            var user = await FindUserByNormalizedUsernameAsync(username, trackChanges: false);

            if (user == null)
            {
                await RejectPersistedSessionAsync(jsRuntime);
                return false;
            }

            var expectedToken = CreateSessionToken(user, UserRole.Candidate);
            if (!IsSameToken(_session.SessionToken, expectedToken))
            {
                await RejectPersistedSessionAsync(jsRuntime);
                return false;
            }

            if (!string.Equals(_session.Username, user.Username, StringComparison.Ordinal) ||
                _session.Role != UserRole.Candidate)
            {
                _session.SignIn(user.Username, UserRole.Candidate, expectedToken);
                await _session.PersistAsync(jsRuntime);
            }

            return true;
        }

        public void Logout()
        {
            _session.SignOut();
        }

        private static string NormalizeUsername(string username)
        {
            return username.Trim().ToLowerInvariant();
        }

        private Task<bool> UsernameExistsAsync(string normalizedUsername)
        {
            return _dbContext.UserAccounts
                .AnyAsync(u => u.Username.Trim().ToLower() == normalizedUsername);
        }

        private Task<UserAccount?> FindUserByNormalizedUsernameAsync(
            string normalizedUsername,
            bool trackChanges = true)
        {
            var query = trackChanges
                ? _dbContext.UserAccounts
                : _dbContext.UserAccounts.AsNoTracking();

            return query.FirstOrDefaultAsync(u => u.Username.Trim().ToLower() == normalizedUsername);
        }

        private string T(string key)
        {
            return _localizer?[key].Value ?? key;
        }

        private async Task RejectPersistedSessionAsync(IJSRuntime jsRuntime)
        {
            _session.SignOut();
            await _session.ClearPersistedAsync(jsRuntime);
        }

        private static string CreateSessionToken(UserAccount user, UserRole role)
        {
            var tokenMaterial = string.Join(
                "|",
                user.Id.ToString(),
                user.Username,
                role.ToString(),
                user.PasswordHash,
                user.CreatedAt.Ticks.ToString());
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenMaterial)));
        }

        private static bool IsSameToken(string actual, string expected)
        {
            var actualBytes = Encoding.UTF8.GetBytes(actual);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            return actualBytes.Length == expectedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
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
    }
}
