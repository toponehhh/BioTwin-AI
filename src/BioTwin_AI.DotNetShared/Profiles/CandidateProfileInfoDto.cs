namespace BioTwin_AI.DotNetShared.Profiles;

public sealed record CandidateProfileInfoVersionDto(
    string InfoType,
    int Version,
    string Source,
    DateTimeOffset UpdatedAt);
