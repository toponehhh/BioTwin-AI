using BioTwin_AI.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BioTwin_AI.Tests.Services
{
    public class CurrentUserSessionTests
    {
        [Fact]
        public void SignIn_WithCandidateRole_SetsUsernameAndRole()
        {
            // Arrange
            var session = new CurrentUserSession();

            // Act
            session.SignIn("testuser", UserRole.Candidate);

            // Assert
            Assert.True(session.IsAuthenticated);
            Assert.Equal("testuser", session.Username);
            Assert.Equal(UserRole.Candidate, session.Role);
            Assert.True(session.IsCandidate);
            Assert.False(session.IsInterviewer);
        }

        [Fact]
        public void SignIn_WithInterviewerRole_SetsUsernameAndRole()
        {
            // Arrange
            var session = new CurrentUserSession();

            // Act
            session.SignIn("interviewer_user", UserRole.Interviewer);

            // Assert
            Assert.True(session.IsAuthenticated);
            Assert.Equal("interviewer_user", session.Username);
            Assert.Equal(UserRole.Interviewer, session.Role);
            Assert.True(session.IsInterviewer);
            Assert.False(session.IsCandidate);
        }

        [Fact]
        public void InterviewerLogin_GeneratesUniqueAnonymousSession()
        {
            // Arrange
            var session1 = new CurrentUserSession();
            var session2 = new CurrentUserSession();

            // Act
            session1.InterviewerLogin();
            session2.InterviewerLogin();

            // Assert
            Assert.True(session1.IsAuthenticated);
            Assert.True(session2.IsAuthenticated);
            Assert.NotEqual(session1.Username, session2.Username); // Each should have unique ID
            Assert.True(session1.IsInterviewer);
            Assert.True(session2.IsInterviewer);
            Assert.StartsWith("interviewer_", session1.Username!);
            Assert.StartsWith("interviewer_", session2.Username!);
        }

        [Fact]
        public void SignOut_ClearsSessionState()
        {
            // Arrange
            var session = new CurrentUserSession();
            session.SignIn("testuser");

            // Act
            session.SignOut();

            // Assert
            Assert.False(session.IsAuthenticated); // Username should be cleared
            Assert.Null(session.Username);
            Assert.True(session.IsCandidate); // Default role is Candidate
            Assert.False(session.IsInterviewer);
        }

        [Fact]
        public void Changed_EventFiredOnSignIn()
        {
            // Arrange
            var session = new CurrentUserSession();
            var eventFired = false;

            session.Changed += () => eventFired = true;

            // Act
            session.SignIn("testuser");

            // Assert
            Assert.True(eventFired);
        }

        [Fact]
        public void DefaultRole_IsCandidate()
        {
            // Arrange
            var session = new CurrentUserSession();

            // Assert
            Assert.Equal(UserRole.Candidate, session.Role);
            Assert.False(session.IsInterviewer);
            Assert.True(session.IsCandidate); // Default is candidate even when not authenticated
        }

        [Fact]
        public void SignIn_DefaultRoleIsCandidateWhenNotSpecified()
        {
            // Arrange
            var session = new CurrentUserSession();

            // Act
            session.SignIn("testuser"); // No role specified, should default to Candidate

            // Assert
            Assert.Equal(UserRole.Candidate, session.Role);
            Assert.True(session.IsCandidate);
        }
    }
}
