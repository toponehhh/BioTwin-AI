using BioTwin_AI.Data;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.Tests.Fixtures
{
    /// <summary>
    /// Helper to create in-memory database context for testing.
    /// </summary>
    public static class DbContextFactory
    {
        public static BioTwinDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<BioTwinDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new BioTwinDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }
    }
}
