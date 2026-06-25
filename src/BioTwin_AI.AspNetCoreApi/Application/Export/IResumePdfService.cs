namespace BioTwin_AI.AspNetCoreApi.Application.Export;

public interface IResumePdfService
{
    byte[] Generate(string markdown, string? title);
}
