using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public sealed class CandidateProfileInfoService(BioTwinApiDbContext dbContext) : ICandidateProfileInfoService
{
    public async Task<CandidateProfileInfo> CreateNextVersionAsync(
        int userId,
        string infoType,
        string jsonData,
        string source,
        int? sourceResumeVersion,
        int? basedOnInfoId,
        int? createdByUserId,
        CancellationToken cancellationToken)
    {
        var normalizedInfoType = NormalizeInfoType(infoType);
        ValidateJson(jsonData);

        var currentItems = await dbContext.CandidateProfileInfos
            .Where(info => info.UserId == userId && info.InfoType == normalizedInfoType && info.IsCurrent)
            .ToListAsync(cancellationToken);
        foreach (var current in currentItems)
        {
            current.IsCurrent = false;
            current.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var nextVersion = await dbContext.CandidateProfileInfos
            .Where(info => info.UserId == userId && info.InfoType == normalizedInfoType)
            .Select(info => (int?)info.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var entity = new CandidateProfileInfo
        {
            UserId = userId,
            InfoType = normalizedInfoType,
            Version = nextVersion + 1,
            JsonData = jsonData,
            Source = string.IsNullOrWhiteSpace(source) ? "llm_generated" : source.Trim(),
            SourceResumeVersion = sourceResumeVersion,
            BasedOnInfoId = basedOnInfoId,
            IsCurrent = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = createdByUserId
        };
        dbContext.CandidateProfileInfos.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public Task<CandidateProfileInfo?> GetCurrentAsync(int userId, string infoType, CancellationToken cancellationToken)
    {
        var normalizedInfoType = NormalizeInfoType(infoType);
        return dbContext.CandidateProfileInfos
            .AsNoTracking()
            .Where(info => info.UserId == userId && info.InfoType == normalizedInfoType && info.IsCurrent)
            .OrderByDescending(info => info.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string NormalizeInfoType(string infoType)
    {
        var normalized = (infoType ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Info type is required.", nameof(infoType));
        }

        return normalized;
    }

    private static void ValidateJson(string jsonData)
    {
        using var _ = JsonDocument.Parse(jsonData);
    }
}
