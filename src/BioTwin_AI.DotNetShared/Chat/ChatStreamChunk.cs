namespace BioTwin_AI.DotNetShared.Chat;

public sealed record ChatStreamChunk(ChatStreamChunkKind Kind, string Content);
