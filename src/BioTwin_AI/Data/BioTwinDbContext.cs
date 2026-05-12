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
        public DbSet<ResumeSection> ResumeSections { get; set; } = null!;
        public DbSet<UserAccount> UserAccounts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ResumeEntry>()
                .HasKey(r => r.Id);

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.Title)
                .IsRequired()
                .HasMaxLength(200);

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.TenantId)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<ResumeEntry>()
                .HasIndex(r => r.TenantId);

            modelBuilder.Entity<ResumeEntry>()
                .HasIndex(r => new { r.TenantId, r.CreatedAt });

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.SourceFileHash)
                .HasMaxLength(64);

            modelBuilder.Entity<ResumeEntry>()
                .HasIndex(r => new { r.TenantId, r.SourceFileHash })
                .IsUnique();

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.SourceFileContent);

            modelBuilder.Entity<ResumeEntry>()
                .Property(r => r.SourceContentType)
                .HasMaxLength(200);

            modelBuilder.Entity<ResumeEntry>()
                .HasMany(r => r.Sections)
                .WithOne(s => s.ResumeEntry)
                .HasForeignKey(s => s.ResumeEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ResumeSection>()
                .HasKey(s => s.Id);

            modelBuilder.Entity<ResumeSection>()
                .Property(s => s.Title)
                .IsRequired();

            modelBuilder.Entity<ResumeSection>()
                .Property(s => s.Content)
                .IsRequired();

            modelBuilder.Entity<ResumeSection>()
                .Property(s => s.HeadingLevel)
                .HasDefaultValue(2);

            modelBuilder.Entity<ResumeSection>()
                .Property(s => s.TenantId)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<ResumeSection>()
                .Property(s => s.EmbeddingPayload)
                .HasColumnName("VectorId");

            modelBuilder.Entity<ResumeSection>()
                .HasIndex(s => s.TenantId);

            modelBuilder.Entity<ResumeSection>()
                .HasIndex(s => new { s.TenantId, s.CreatedAt });

            modelBuilder.Entity<ResumeSection>()
                .HasIndex(s => new { s.ResumeEntryId, s.SortOrder });

            modelBuilder.Entity<ResumeSection>()
                .HasOne(s => s.ParentSection)
                .WithMany(s => s.ChildSections)
                .HasForeignKey(s => s.ParentSectionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ResumeSection>()
                .HasIndex(s => s.ParentSectionId);

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
