using BioTwin_AI.DotNetShared.Rag;

namespace BioTwin_AI.DotNetShared.Chat;

public sealed record ChatResponse(
    string Answer,
    IReadOnlyList<RagCitationDto> Citations);
