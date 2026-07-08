namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class UserAccount
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string Avatar { get; set; } = "🧑‍💻";

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = BioTwin_AI.DotNetShared.Auth.UserRole.Candidate.ToString();

    public string ProfileHash { get; set; } = string.Empty;

    public DateTimeOffset ProfileHashUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int CandidateProfileVersion { get; set; } = 1;

    public bool IsProfilePublic { get; set; } = true;

    public bool IsDefaultCandidate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public List<UserExternalIdentity> ExternalIdentities { get; set; } = [];

    public List<UserRoleAssignment> Roles { get; set; } = [];

    public List<CandidateProfileInfo> ProfileInfos { get; set; } = [];
}
