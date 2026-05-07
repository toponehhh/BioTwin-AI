using Microsoft.EntityFrameworkCore;
using BioTwin_AI.Models;

namespace BioTwin_AI.Data
{
    public class BioTwinDbContext : DbContext
    {
        public BioTwinDbContext(DbContextOptions<BioTwinDbContext> options) : base(options)
        {
        }

        public DbSet<ResumeEntry> ResumeEntries { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ResumeEntry>()
                .HasKey(r => r.Id);

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.Title)
                .IsRequired();

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.Content)
                .IsRequired();
        }
    }
}
