namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed class LlmResponseException : InvalidOperationException
{
    public LlmResponseException(string message)
        : base(message)
    {
    }
}
