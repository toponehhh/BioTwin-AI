namespace BioTwin_AI.BlazorClient.Models;

public sealed record PendingResumeUpload(
    string FileName,
    string ContentType,
    long Size,
    byte[] Content);
