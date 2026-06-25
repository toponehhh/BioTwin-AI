namespace BioTwin_AI.DotNetShared.Auth;

public sealed record RegisterRequest(
    string Username,
    string Password,
    string? Nickname = null,
    string? Avatar = null);
