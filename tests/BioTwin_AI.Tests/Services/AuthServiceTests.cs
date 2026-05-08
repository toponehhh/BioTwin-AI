using BioTwin_AI.Services;
using BioTwin_AI.Tests.Fixtures;
using Microsoft.Extensions.Logging;
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
    }
}
