using BioTwin_AI.Data;
using BioTwin_AI.Models;
using BioTwin_AI.Tests.Fixtures;
using Xunit;

namespace BioTwin_AI.Tests.Integration
{
    /// <summary>
    /// Integration tests for database operations and multi-tenant scenarios.
    /// </summary>
    public class MultiTenantIntegrationTests
    {
        [Fact]
        public async Task ResumeEntry_MultiTenantIsolation()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();

            var candidate1Resume = new ResumeEntry
            {
                TenantId = "candidate1",
                Title = "C# Experience",
                Content = "5 years C# development",
                SourceFileName = "cv.pdf"
            };

            var candidate2Resume = new ResumeEntry
            {
                TenantId = "candidate2",
                Title = "Python Experience",
                Content = "3 years Python development",
                SourceFileName = "cv.pdf"
            };

            // Act
            dbContext.ResumeEntries.Add(candidate1Resume);
            dbContext.ResumeEntries.Add(candidate2Resume);
            await dbContext.SaveChangesAsync();

            // Assert
            var candidate1Entries = dbContext.ResumeEntries.Where(e => e.TenantId == "candidate1").ToList();
            var candidate2Entries = dbContext.ResumeEntries.Where(e => e.TenantId == "candidate2").ToList();

            Assert.Single(candidate1Entries);
            Assert.Single(candidate2Entries);
            Assert.Equal("C# Experience", candidate1Entries[0].Title);
            Assert.Equal("Python Experience", candidate2Entries[0].Title);
        }

        [Fact]
        public async Task UserAccount_UniqueUsernameConstraint()
        {
            // Arrange - Verify the model configuration has unique constraint
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var userAccountType = dbContext.Model.FindEntityType(typeof(UserAccount));
            var usernameIndex = userAccountType?.GetIndexes()
                .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Username"));

            // Assert
            Assert.NotNull(usernameIndex);
            Assert.True(usernameIndex!.IsUnique, "Username should be configured as unique in the model");
        }

        [Fact]
        public async Task ResumeEntry_WithEmbedding_PersistsCorrectly()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();
            var embeddingPayload = "[0.1,0.2,0.3,0.4,0.5]";

            var resumeEntry = new ResumeEntry
            {
                TenantId = "candidate1",
                Title = "Experience",
                Content = "My experience",
                EmbeddingPayload = embeddingPayload,
                SourceFileName = "cv.pdf"
            };

            // Act
            dbContext.ResumeEntries.Add(resumeEntry);
            await dbContext.SaveChangesAsync();

            // Assert
            var retrieved = dbContext.ResumeEntries.First(e => e.Id == resumeEntry.Id);
            Assert.Equal(embeddingPayload, retrieved.EmbeddingPayload);
        }

        [Fact]
        public async Task ResumeEntry_OrderByCreatedAt()
        {
            // Arrange
            var dbContext = DbContextFactory.CreateInMemoryContext();

            var resume1 = new ResumeEntry { TenantId = "candidate1", Title = "Old Resume", Content = "Old", SourceFileName = "old.pdf", CreatedAt = DateTime.UtcNow.AddDays(-10) };
            var resume2 = new ResumeEntry { TenantId = "candidate1", Title = "New Resume", Content = "New", SourceFileName = "new.pdf", CreatedAt = DateTime.UtcNow };

            // Act
            dbContext.ResumeEntries.Add(resume1);
            dbContext.ResumeEntries.Add(resume2);
            await dbContext.SaveChangesAsync();

            // Assert
            var ordered = dbContext.ResumeEntries
                .Where(e => e.TenantId == "candidate1")
                .OrderByDescending(e => e.CreatedAt)
                .ToList();

            Assert.Equal("New Resume", ordered[0].Title);
            Assert.Equal("Old Resume", ordered[1].Title);
        }
    }
}
