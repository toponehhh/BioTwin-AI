namespace BioTwin_AI.Models
{
    /// <summary>
    /// Application user account for lightweight login.
    /// </summary>
    public class UserAccount
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}