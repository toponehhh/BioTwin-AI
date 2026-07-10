using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeStateTokenService(BioTwinApiDbContext dbContext) : IResumeStateTokenService
{
    public async Task<string> ComputeAsync(string tenantId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ResumeEntries
            .AsNoTracking()
            .Where(entry => entry.TenantId == tenantId)
            .OrderBy(entry => entry.Language)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Id,
                entry.Language,
                entry.UpdatedAt,
                entry.SourceFileHash
            })
            .ToListAsync(cancellationToken);

        var canonical = new StringBuilder();
        foreach (var row in rows)
        {
            Append(canonical, row.Id.ToString(CultureInfo.InvariantCulture));
            Append(canonical, ResumeLanguages.Normalize(row.Language));
            Append(canonical, row.UpdatedAt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
            Append(canonical, row.SourceFileHash?.Trim().ToLowerInvariant() ?? string.Empty);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void Append(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
        target.Append(';');
    }
}
