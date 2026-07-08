using System.Security.Cryptography;
using System.Text;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public sealed class ProfileShareCodeGenerator : IProfileShareCodeGenerator
{
    public const int DefaultLength = 8;

    public const int MinimumLength = 6;

    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Generate(int length = DefaultLength)
    {
        if (length < MinimumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Profile share codes must be at least {MinimumLength} characters.");
        }

        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(Alphabet.Length);
            builder.Append(Alphabet[index]);
        }

        return builder.ToString();
    }

    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
