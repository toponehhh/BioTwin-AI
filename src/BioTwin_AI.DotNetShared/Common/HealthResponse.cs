namespace BioTwin_AI.DotNetShared.Common;

public sealed record HealthResponse(
    string Status,
    DateTimeOffset CheckedAt);
