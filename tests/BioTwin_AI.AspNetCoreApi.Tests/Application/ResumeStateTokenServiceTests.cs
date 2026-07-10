using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeStateTokenServiceTests
{
    [Fact]
    public async Task ComputeAsync_is_deterministic_and_changes_with_resume_state()
    {
        await using var context = CreateContext();
        var service = new ResumeStateTokenService(context);

        var emptyToken = await service.ComputeAsync("huangd", CancellationToken.None);
        context.ResumeEntries.Add(CreateResume("huangd", ResumeLanguages.English, 1));
        await context.SaveChangesAsync();

        var populatedToken = await service.ComputeAsync("huangd", CancellationToken.None);

        Assert.NotEqual(emptyToken, populatedToken);
        Assert.Equal(populatedToken, await service.ComputeAsync("huangd", CancellationToken.None));
        Assert.DoesNotContain('=', populatedToken);
    }

    [Fact]
    public async Task ComputeAsync_is_order_independent_and_tenant_scoped()
    {
        await using var firstContext = CreateContext();
        firstContext.ResumeEntries.AddRange(
            CreateResume("huangd", ResumeLanguages.SimplifiedChinese, 2),
            CreateResume("huangd", ResumeLanguages.English, 1),
            CreateResume("another", ResumeLanguages.English, 3));
        await firstContext.SaveChangesAsync();

        await using var secondContext = CreateContext();
        secondContext.ResumeEntries.AddRange(
            CreateResume("huangd", ResumeLanguages.English, 1),
            CreateResume("huangd", ResumeLanguages.SimplifiedChinese, 2));
        await secondContext.SaveChangesAsync();

        Assert.Equal(
            await new ResumeStateTokenService(firstContext).ComputeAsync("huangd", CancellationToken.None),
            await new ResumeStateTokenService(secondContext).ComputeAsync("huangd", CancellationToken.None));
    }

    [Fact]
    public async Task ComputeAsync_changes_when_tracked_state_changes()
    {
        await using var context = CreateContext();
        var resume = CreateResume("huangd", ResumeLanguages.English, 1);
        context.ResumeEntries.Add(resume);
        await context.SaveChangesAsync();
        var service = new ResumeStateTokenService(context);
        var before = await service.ComputeAsync("huangd", CancellationToken.None);

        resume.SourceFileHash = "changed";
        resume.UpdatedAt = resume.UpdatedAt.AddMinutes(1);
        await context.SaveChangesAsync();

        Assert.NotEqual(before, await service.ComputeAsync("huangd", CancellationToken.None));
    }

    private static ResumeEntry CreateResume(string tenantId, string language, int id)
    {
        return new ResumeEntry
        {
            Id = id,
            TenantId = tenantId,
            Language = language,
            Title = $"Resume {id}",
            SourceFileHash = $"hash-{id}",
            CreatedAt = new DateTimeOffset(2026, 7, id, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 7, id, 1, 0, 0, TimeSpan.Zero)
        };
    }

    private static BioTwinApiDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new BioTwinApiDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
