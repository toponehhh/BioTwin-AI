namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

public sealed class UserRoleAssignment
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public UserAccount? User { get; set; }

    public string Role { get; set; } = BioTwin_AI.DotNetShared.Auth.UserRole.Candidate.ToString();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
