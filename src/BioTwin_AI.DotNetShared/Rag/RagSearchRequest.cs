namespace BioTwin_AI.DotNetShared.Rag;

public sealed record RagSearchRequest(string Query, int Limit = 5);
