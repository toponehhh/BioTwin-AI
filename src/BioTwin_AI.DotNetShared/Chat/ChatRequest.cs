namespace BioTwin_AI.DotNetShared.Chat;

public sealed record ChatRequest(
    string Question,
    IReadOnlyList<ChatMessageDto>? History = null);
