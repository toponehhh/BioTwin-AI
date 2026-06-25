namespace BioTwin_AI.DotNetShared.Logging;

public sealed record ClientLogEntryRequest(
    string Level,
    string Category,
    string Message,
    string? Exception,
    string? Url,
    DateTimeOffset Timestamp);
