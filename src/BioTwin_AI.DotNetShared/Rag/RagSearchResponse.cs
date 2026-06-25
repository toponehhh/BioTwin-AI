namespace BioTwin_AI.DotNetShared.Rag;

public sealed record RagSearchResponse(IReadOnlyList<RagCitationDto> Results);
