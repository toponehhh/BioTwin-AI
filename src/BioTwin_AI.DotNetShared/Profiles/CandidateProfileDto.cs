namespace BioTwin_AI.DotNetShared.Profiles;

public sealed record CandidateProfileDto(
    CandidateProfileCandidateDto Candidate,
    IReadOnlyList<CareerTimelineItemDto> CareerTimelineItems,
    IReadOnlyList<WorkTimelineItemDto> WorkTimelineItems,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Education,
    IReadOnlyList<string> Certifications,
    string? PublicContact,
    IReadOnlyList<CandidateProfileInfoVersionDto> InfoVersions);

public sealed record CandidateProfileCandidateDto(
    string DisplayName,
    string Headline,
    string Avatar,
    string Summary,
    DateTimeOffset ProfileHashUpdatedAt,
    int CandidateProfileVersion);

public sealed record CareerTimelineItemDto(
    string Title,
    string Subtitle,
    string PeriodLabel,
    int SortOrder,
    string Description,
    bool IsHighlighted);

public sealed record WorkTimelineItemDto(
    string Number,
    string Title,
    string Description,
    string? ImageUrl,
    string? LinkUrl,
    int SortOrder);
