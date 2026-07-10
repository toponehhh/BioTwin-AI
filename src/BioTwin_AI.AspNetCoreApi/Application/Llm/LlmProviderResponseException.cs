namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed class LlmProviderResponseException(string message) : Exception(message);
