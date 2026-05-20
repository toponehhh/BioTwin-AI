using BioTwin_AI.Services;
using BioTwin_AI.Models;
using BioTwin_AI.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class AuthServiceTests
    {
        [Fact]
        public async Task RegisterAsync_WithValidCredentials_CreatesUserAndSignsIn()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            // Act
            var result = await authService.RegisterAsync("testuser", "password123");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Registered and signed in successfully.", result.Message);
            Assert.True(session.IsAuthenticated);
            Assert.Equal("testuser", session.Username);
        }

        [Fact]
        public async Task RegisterAsync_WithMissingUsername_ReturnsFalse()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            // Act
            var result = await authService.RegisterAsync("", "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Username and password are required.", result.Message);
        }

        [Fact]
        public async Task RegisterAsync_WithDuplicateUsername_ReturnsFalse()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            await authService.RegisterAsync("testuser", "password123");

            // Act
            var result = await authService.RegisterAsync("testuser", "password456");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Username already exists.", result.Message);
        }

        [Fact]
        public async Task RegisterAsync_WithExistingUsernameDifferentCase_ReturnsFalse()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            dbContext.UserAccounts.Add(new UserAccount
            {
                Username = "ExistingUser",
                PasswordHash = "legacy-hash",
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            // Act
            var result = await authService.RegisterAsync("existinguser", "password456");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Username already exists.", result.Message);
            Assert.False(session.IsAuthenticated);
            Assert.Single(dbContext.UserAccounts);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentials_SignsInUser()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            await authService.RegisterAsync("testuser", "password123");
            session.SignOut(); // Sign out first

            // Act
            var result = await authService.LoginAsync("testuser", "password123");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("Logged in successfully.", result.Message);
            Assert.True(session.IsAuthenticated);
            Assert.Equal("testuser", session.Username);
        }

        [Fact]
        public async Task LoginAsync_WithIncorrectPassword_ReturnsFalse()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            await authService.RegisterAsync("testuser", "password123");
            session.SignOut();

            // Act
            var result = await authService.LoginAsync("testuser", "wrongpassword");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Invalid username or password.", result.Message);
            Assert.False(session.IsAuthenticated);
        }

        [Fact]
        public async Task LoginAsync_WithNonexistentUser_ReturnsFalse()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            // Act
            var result = await authService.LoginAsync("nonexistent", "password123");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Invalid username or password.", result.Message);
        }

        [Fact]
        public void Logout_ClearsUserSession()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            session.SignIn("testuser");
            var authService = new AuthService(dbContext, session);

            // Act
            authService.Logout();

            // Assert
            Assert.False(session.IsAuthenticated);
            Assert.Null(session.Username);
        }

        [Fact]
        public async Task RegisterAsync_NormalizeUsernameToLowercase()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);

            // Act
            var result = await authService.RegisterAsync("TestUser", "password123");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("testuser", session.Username); // Should be lowercase
        }

        [Fact]
        public async Task RestoreSessionAsync_WithDeletedCandidateUser_ClearsSessionAndPersistedStorage()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);
            var jsRuntime = new FakeStorageJsRuntime();
            jsRuntime.SetItem("biotwin.currentUser", "deleteduser");
            jsRuntime.SetItem("biotwin.userRole", UserRole.Candidate.ToString());

            // Act
            var restored = await authService.RestoreSessionAsync(jsRuntime);

            // Assert
            Assert.False(restored);
            Assert.False(session.IsAuthenticated);
            Assert.Null(session.Username);
            Assert.Null(jsRuntime.GetItem("biotwin.currentUser"));
            Assert.Null(jsRuntime.GetItem("biotwin.userRole"));
        }

        [Fact]
        public async Task RestoreSessionAsync_WithExistingCandidateUser_RestoresSession()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);
            var jsRuntime = new FakeStorageJsRuntime();

            await authService.RegisterAsync("testuser", "password123");
            await session.PersistAsync(jsRuntime);
            authService.Logout();

            // Act
            var restored = await authService.RestoreSessionAsync(jsRuntime);

            // Assert
            Assert.True(restored);
            Assert.True(session.IsAuthenticated);
            Assert.Equal("testuser", session.Username);
            Assert.Equal(UserRole.Candidate, session.Role);
        }

        [Fact]
        public async Task RestoreSessionAsync_WithChangedCandidateCredentials_ClearsSessionAndPersistedStorage()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var session = new CurrentUserSession();
            var authService = new AuthService(dbContext, session);
            var jsRuntime = new FakeStorageJsRuntime();

            await authService.RegisterAsync("testuser", "password123");
            await session.PersistAsync(jsRuntime);
            authService.Logout();

            var user = dbContext.UserAccounts.Single(u => u.Username == "testuser");
            user.PasswordHash = "changed-database-value";
            await dbContext.SaveChangesAsync();

            // Act
            var restored = await authService.RestoreSessionAsync(jsRuntime);

            // Assert
            Assert.False(restored);
            Assert.False(session.IsAuthenticated);
            Assert.Null(session.Username);
            Assert.Null(jsRuntime.GetItem("biotwin.currentUser"));
            Assert.Null(jsRuntime.GetItem("biotwin.userRole"));
        }

        private sealed class FakeStorageJsRuntime : IJSRuntime
        {
            private readonly Dictionary<string, string> _storage = new();

            public void SetItem(string key, string value)
            {
                _storage[key] = value;
            }

            public string? GetItem(string key)
            {
                return _storage.TryGetValue(key, out var value) ? value : null;
            }

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            {
                return InvokeCore<TValue>(identifier, args);
            }

            public ValueTask<TValue> InvokeAsync<TValue>(
                string identifier,
                CancellationToken cancellationToken,
                object?[]? args)
            {
                return InvokeCore<TValue>(identifier, args);
            }

            private ValueTask<TValue> InvokeCore<TValue>(string identifier, object?[]? args)
            {
                var key = args?.FirstOrDefault() as string
                    ?? throw new InvalidOperationException("Storage key was not provided.");

                if (identifier == "localStorage.getItem")
                {
                    var value = (TValue)(object?)GetItem(key)!;
                    return new ValueTask<TValue>(value);
                }

                if (identifier == "localStorage.setItem")
                {
                    var value = args?.Skip(1).FirstOrDefault()?.ToString() ?? string.Empty;
                    _storage[key] = value;
                    return new ValueTask<TValue>(default(TValue)!);
                }

                if (identifier == "localStorage.removeItem")
                {
                    _storage.Remove(key);
                    return new ValueTask<TValue>(default(TValue)!);
                }

                throw new NotSupportedException(identifier);
            }
        }
    }
}
