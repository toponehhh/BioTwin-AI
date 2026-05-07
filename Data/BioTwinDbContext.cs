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
        public DbSet<UserAccount> UserAccounts { get; set; } = null!;

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

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.TenantId)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<ResumeEntry>()
                .HasIndex(r => r.TenantId);

            modelBuilder.Entity<ResumeEntry>()
                .HasIndex(r => new { r.TenantId, r.CreatedAt });

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.EmbeddingPayload)
                .HasColumnName("VectorId");

            modelBuilder.Entity<UserAccount>()
                .HasKey(u => u.Id);

            modelBuilder.Entity<UserAccount>()
                .Property(u => u.Username)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<UserAccount>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<UserAccount>()
                .Property(u => u.PasswordHash)
                .IsRequired();
        }
    }
}
