namespace BioTwin_AI.DotNetShared.Auth;

public sealed record AuthResult(
    bool Success,
    string Message,
    CurrentSessionResponse? Session);
