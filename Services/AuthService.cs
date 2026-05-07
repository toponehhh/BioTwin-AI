using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace BioTwin_AI.Services
{
    public class AuthService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly CurrentUserSession _session;

        public AuthService(BioTwinDbContext dbContext, CurrentUserSession session)
        {
            _dbContext = dbContext;
            _session = session;
        }

        public async Task<(bool Success, string Message)> RegisterAsync(string username, string password)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Username and password are required.");
            }

            var exists = await _dbContext.UserAccounts.AnyAsync(u => u.Username == username);
            if (exists)
            {
                return (false, "Username already exists.");
            }

            var user = new UserAccount
            {
                Username = username,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.UserAccounts.Add(user);
            await _dbContext.SaveChangesAsync();
            _session.SignIn(username);

            return (true, "Registered and signed in successfully.");
        }

        public async Task<(bool Success, string Message)> LoginAsync(string username, string password)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Username and password are required.");
            }

            var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
            {
                return (false, "Invalid username or password.");
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                return (false, "Invalid username or password.");
            }

            _session.SignIn(user.Username);
            return (true, "Logged in successfully.");
        }

        public void Logout()
        {
            _session.SignOut();
        }

        private static string NormalizeUsername(string username)
        {
            return username.Trim().ToLowerInvariant();
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