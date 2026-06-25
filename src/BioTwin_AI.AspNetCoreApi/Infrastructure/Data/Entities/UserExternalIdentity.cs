namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class UserExternalIdentity
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public UserAccount? User { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string ProviderUserId { get; set; } = string.Empty;

    public string? ProviderEmail { get; set; }

    public bool ProviderEmailVerified { get; set; }

    public string? ProviderDisplayName { get; set; }

    public string? ProviderAvatarUrl { get; set; }

    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public string? RawClaimsJson { get; set; }
}
