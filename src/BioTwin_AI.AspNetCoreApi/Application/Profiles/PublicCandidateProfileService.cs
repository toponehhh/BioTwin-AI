using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Profiles;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public sealed class PublicCandidateProfileService(BioTwinApiDbContext dbContext) : IPublicCandidateProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CandidateProfileDto?> GetAsync(string? uid, CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(uid)
            ? await ResolveDefaultAsync(cancellationToken)
            : await ResolveByUidAsync(uid, cancellationToken);

        if (user is null || !CanShowPublicCandidate(user))
        {
            return null;
        }

        var currentInfos = user.ProfileInfos
            .Where(info => info.IsCurrent)
            .GroupBy(info => info.InfoType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(info => info.Version).First(),
                StringComparer.OrdinalIgnoreCase);

        return new CandidateProfileDto(
            Candidate: new CandidateProfileCandidateDto(
                DisplayName: string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname,
                Headline: "Candidate Profile",
                Avatar: string.IsNullOrWhiteSpace(user.Avatar) ? "🧑‍💻" : user.Avatar,
                Summary: "Public candidate profile generated from resume content.",
                ProfileHashUpdatedAt: user.ProfileHashUpdatedAt,
                CandidateProfileVersion: user.CandidateProfileVersion),
            CareerTimelineItems: ReadInfo<CareerTimelineItemDto>(currentInfos, "careertimeline"),
            WorkTimelineItems: ReadInfo<WorkTimelineItemDto>(currentInfos, "worktimeline"),
            Skills: [],
            Education: [],
            Certifications: [],
            PublicContact: null,
            InfoVersions: currentInfos.Values
                .OrderBy(info => info.InfoType)
                .Select(info => new CandidateProfileInfoVersionDto(info.InfoType, info.Version, info.Source, info.UpdatedAt))
                .ToArray());
    }

    private Task<UserAccount?> ResolveByUidAsync(string uid, CancellationToken cancellationToken)
    {
        var normalized = ProfileShareCodeGenerator.Normalize(uid);
        return QueryPublicCandidates()
            .FirstOrDefaultAsync(user => user.ProfileHash == normalized, cancellationToken);
    }

    private async Task<UserAccount?> ResolveDefaultAsync(CancellationToken cancellationToken)
    {
        var explicitDefault = await QueryPublicCandidates()
            .OrderBy(user => user.Id)
            .FirstOrDefaultAsync(user => user.IsDefaultCandidate, cancellationToken);
        if (explicitDefault is not null)
        {
            return explicitDefault;
        }

        return await QueryPublicCandidates()
            .OrderBy(user => user.Id)
            .FirstOrDefaultAsync(user => user.Roles.Any(role => role.Role == UserRole.Admin.ToString())
                && user.Roles.Any(role => role.Role == UserRole.Candidate.ToString()), cancellationToken);
    }

    private IQueryable<UserAccount> QueryPublicCandidates()
    {
        return dbContext.UserAccounts
            .AsNoTracking()
            .Include(user => user.Roles)
            .Include(user => user.ProfileInfos)
            .Where(user => !user.IsDeleted && user.IsProfilePublic);
    }

    private static bool CanShowPublicCandidate(UserAccount user)
    {
        return user.Roles.Any(role => string.Equals(role.Role, UserRole.Candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            || string.Equals(user.Role, UserRole.Candidate.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<T> ReadInfo<T>(
        IReadOnlyDictionary<string, CandidateProfileInfo> infos,
        string infoType)
    {
        if (!infos.TryGetValue(infoType, out var info))
        {
            return [];
        }

        return JsonSerializer.Deserialize<IReadOnlyList<T>>(info.JsonData, JsonOptions) ?? [];
    }
}
