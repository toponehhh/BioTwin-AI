namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class UserAccount
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string Avatar { get; set; } = "🧑‍💻";

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = BioTwin_AI.DotNetShared.Auth.UserRole.Candidate.ToString();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public List<UserExternalIdentity> ExternalIdentities { get; set; } = [];
}
