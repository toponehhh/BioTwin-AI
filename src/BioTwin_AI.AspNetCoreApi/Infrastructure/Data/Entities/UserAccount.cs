namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class UserAccount
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = BioTwin_AI.DotNetShared.Auth.UserRole.Candidate.ToString();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<UserExternalIdentity> ExternalIdentities { get; set; } = [];
}
