namespace BioTwin_AI.DotNetShared.Common;

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);
