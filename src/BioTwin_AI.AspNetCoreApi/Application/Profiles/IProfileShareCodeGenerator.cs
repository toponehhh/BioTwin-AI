namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public interface IProfileShareCodeGenerator
{
    string Generate(int length = ProfileShareCodeGenerator.DefaultLength);
}
