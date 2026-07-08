using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data;

public sealed class BioTwinApiDbContext(DbContextOptions<BioTwinApiDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<UserExternalIdentity> UserExternalIdentities => Set<UserExternalIdentity>();

    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();

    public DbSet<CandidateProfileInfo> CandidateProfileInfos => Set<CandidateProfileInfo>();

    public DbSet<ResumeEntry> ResumeEntries => Set<ResumeEntry>();

    public DbSet<ResumeSection> ResumeSections => Set<ResumeSection>();

    public DbSet<ResumeSectionVector> ResumeSectionVectors => Set<ResumeSectionVector>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).IsRequired().HasMaxLength(100);
            entity.Property(user => user.Nickname).IsRequired().HasMaxLength(100);
            entity.Property(user => user.Avatar).IsRequired().HasMaxLength(32);
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).IsRequired().HasMaxLength(40);
            entity.Property(user => user.ProfileHash).IsRequired().HasMaxLength(16);
            entity.Property(user => user.ProfileHashUpdatedAt).IsRequired();
            entity.Property(user => user.CandidateProfileVersion).IsRequired();
            entity.Property(user => user.IsProfilePublic).IsRequired();
            entity.Property(user => user.IsDefaultCandidate).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
            entity.Property(user => user.UpdatedAt).IsRequired();
            entity.Property(user => user.IsDeleted).IsRequired();
            entity.HasIndex(user => user.Username).IsUnique();
            entity.HasIndex(user => user.ProfileHash)
                .HasDatabaseName("IX_UserAccounts_ProfileHash")
                .IsUnique()
                .HasFilter("ProfileHash <> ''");
            entity.HasIndex(user => user.IsDefaultCandidate)
                .HasDatabaseName("IX_UserAccounts_DefaultCandidate")
                .IsUnique()
                .HasFilter("IsDefaultCandidate = 1 AND IsDeleted = 0");
            entity.HasQueryFilter(user => !user.IsDeleted);
        });

        modelBuilder.Entity<UserRoleAssignment>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Role).IsRequired().HasMaxLength(40);
            entity.Property(role => role.CreatedAt).IsRequired();
            entity.HasIndex(role => new { role.UserId, role.Role }).IsUnique();
            entity.HasQueryFilter(role => !role.User!.IsDeleted);
            entity.HasOne(role => role.User)
                .WithMany(user => user.Roles)
                .HasForeignKey(role => role.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CandidateProfileInfo>(entity =>
        {
            entity.HasKey(info => info.Id);
            entity.Property(info => info.InfoType).IsRequired().HasMaxLength(80);
            entity.Property(info => info.JsonData).IsRequired();
            entity.Property(info => info.Source).IsRequired().HasMaxLength(40);
            entity.Property(info => info.ModelName).HasMaxLength(120);
            entity.Property(info => info.PromptVersion).HasMaxLength(80);
            entity.Property(info => info.IsCurrent).IsRequired();
            entity.Property(info => info.CreatedAt).IsRequired();
            entity.Property(info => info.UpdatedAt).IsRequired();
            entity.HasIndex(info => new { info.UserId, info.InfoType, info.Version }).IsUnique();
            entity.HasIndex(info => new { info.UserId, info.InfoType, info.IsCurrent });
            entity.HasQueryFilter(info => !info.User!.IsDeleted);
            entity.HasOne(info => info.User)
                .WithMany(user => user.ProfileInfos)
                .HasForeignKey(info => info.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(info => info.BasedOnInfo)
                .WithMany()
                .HasForeignKey(info => info.BasedOnInfoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserExternalIdentity>(entity =>
        {
            entity.HasKey(identity => identity.Id);
            entity.Property(identity => identity.Provider).IsRequired().HasMaxLength(80);
            entity.Property(identity => identity.ProviderUserId).IsRequired().HasMaxLength(200);
            entity.Property(identity => identity.ProviderEmail).HasMaxLength(320);
            entity.Property(identity => identity.ProviderDisplayName).HasMaxLength(200);
            entity.Property(identity => identity.ProviderAvatarUrl).HasMaxLength(1000);
            entity.Property(identity => identity.CreatedAt).IsRequired();
            entity.Property(identity => identity.UpdatedAt).IsRequired();
            entity.HasIndex(identity => new { identity.Provider, identity.ProviderUserId }).IsUnique();
            entity.HasQueryFilter(identity => !identity.User!.IsDeleted);
            entity.HasOne(identity => identity.User)
                .WithMany(user => user.ExternalIdentities)
                .HasForeignKey(identity => identity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResumeEntry>(entity =>
        {
            entity.HasKey(resume => resume.Id);
            entity.Property(resume => resume.TenantId).IsRequired().HasMaxLength(100);
            entity.Property(resume => resume.Language).IsRequired().HasMaxLength(10);
            entity.Property(resume => resume.Title).IsRequired().HasMaxLength(200);
            entity.Property(resume => resume.SourceFileName).HasMaxLength(260);
            entity.Property(resume => resume.SourceContentType).HasMaxLength(200);
            entity.Property(resume => resume.SourceFileHash).HasMaxLength(64);
            entity.Property(resume => resume.CreatedAt).IsRequired();
            entity.Property(resume => resume.UpdatedAt).IsRequired();
            entity.HasIndex(resume => new { resume.TenantId, resume.Language }).IsUnique();
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
            entity.Property(section => section.CreatedAt).IsRequired();
            entity.Property(section => section.UpdatedAt).IsRequired();
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
            entity.Property(vector => vector.CreatedAt).IsRequired();
            entity.Property(vector => vector.UpdatedAt).IsRequired();
            entity.HasIndex(vector => vector.ResumeSectionId).IsUnique();
            entity.HasIndex(vector => vector.TenantId);
        });
    }
}
