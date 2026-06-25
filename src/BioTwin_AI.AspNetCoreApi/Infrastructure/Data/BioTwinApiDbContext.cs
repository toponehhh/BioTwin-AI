using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data;

public sealed class BioTwinApiDbContext(DbContextOptions<BioTwinApiDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<UserExternalIdentity> UserExternalIdentities => Set<UserExternalIdentity>();

    public DbSet<ResumeEntry> ResumeEntries => Set<ResumeEntry>();

    public DbSet<ResumeSection> ResumeSections => Set<ResumeSection>();

    public DbSet<ResumeSectionVector> ResumeSectionVectors => Set<ResumeSectionVector>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).IsRequired().HasMaxLength(100);
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).IsRequired().HasMaxLength(40);
            entity.HasIndex(user => user.Username).IsUnique();
        });

        modelBuilder.Entity<UserExternalIdentity>(entity =>
        {
            entity.HasKey(identity => identity.Id);
            entity.Property(identity => identity.Provider).IsRequired().HasMaxLength(80);
            entity.Property(identity => identity.ProviderUserId).IsRequired().HasMaxLength(200);
            entity.Property(identity => identity.ProviderEmail).HasMaxLength(320);
            entity.Property(identity => identity.ProviderDisplayName).HasMaxLength(200);
            entity.Property(identity => identity.ProviderAvatarUrl).HasMaxLength(1000);
            entity.HasIndex(identity => new { identity.Provider, identity.ProviderUserId }).IsUnique();
            entity.HasOne(identity => identity.User)
                .WithMany(user => user.ExternalIdentities)
                .HasForeignKey(identity => identity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResumeEntry>(entity =>
        {
            entity.HasKey(resume => resume.Id);
            entity.Property(resume => resume.TenantId).IsRequired().HasMaxLength(100);
            entity.Property(resume => resume.Title).IsRequired().HasMaxLength(200);
            entity.Property(resume => resume.SourceFileName).HasMaxLength(260);
            entity.Property(resume => resume.SourceContentType).HasMaxLength(200);
            entity.Property(resume => resume.SourceFileHash).HasMaxLength(64);
            entity.HasIndex(resume => new { resume.TenantId, resume.CreatedAt });
            entity.HasIndex(resume => new { resume.TenantId, resume.SourceFileHash }).IsUnique();
            entity.HasMany(resume => resume.Sections)
                .WithOne(section => section.ResumeEntry)
                .HasForeignKey(section => section.ResumeEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResumeSection>(entity =>
        {
            entity.HasKey(section => section.Id);
            entity.Property(section => section.TenantId).IsRequired().HasMaxLength(100);
            entity.Property(section => section.Title).IsRequired();
            entity.Property(section => section.Content).IsRequired();
            entity.HasIndex(section => new { section.ResumeEntryId, section.SortOrder });
            entity.HasOne(section => section.ParentSection)
                .WithMany(section => section.ChildSections)
                .HasForeignKey(section => section.ParentSectionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(section => section.Vector)
                .WithOne(vector => vector.ResumeSection)
                .HasForeignKey<ResumeSectionVector>(vector => vector.ResumeSectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResumeSectionVector>(entity =>
        {
            entity.HasKey(vector => vector.Id);
            entity.Property(vector => vector.TenantId).IsRequired().HasMaxLength(100);
            entity.Property(vector => vector.ResumeTitle).IsRequired();
            entity.Property(vector => vector.SectionTitle).IsRequired();
            entity.Property(vector => vector.Content).IsRequired();
            entity.Property(vector => vector.EmbeddingPayload).IsRequired();
            entity.HasIndex(vector => vector.ResumeSectionId).IsUnique();
            entity.HasIndex(vector => vector.TenantId);
        });
    }
}
