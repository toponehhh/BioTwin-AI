namespace BioTwin_AI.DotNetShared.Resumes;

public static class ResumeLanguages
{
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en";

    public static readonly string[] Supported = [SimplifiedChinese, English];

    public static bool IsSupported(string? language)
    {
        return Supported.Contains(language, StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string? language)
    {
        return string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : SimplifiedChinese;
    }
}
