using BioTwin_AI.AspNetCoreApi.Application.Profiles;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ProfileShareCodeGeneratorTests
{
    [Fact]
    public void Generate_returns_default_eight_character_unambiguous_code()
    {
        var generator = new ProfileShareCodeGenerator();

        var code = generator.Generate();

        Assert.Equal(8, code.Length);
        Assert.Equal(code.ToUpperInvariant(), code);
        Assert.All(code, ch => Assert.Contains(ch, ProfileShareCodeGenerator.Alphabet));
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('L', code);
    }

    [Theory]
    [InlineData(" k7x4q9mf ", "K7X4Q9MF")]
    [InlineData("abc-def 23", "ABCDEF23")]
    public void Normalize_removes_separators_and_uppercases_code(string input, string expected)
    {
        Assert.Equal(expected, ProfileShareCodeGenerator.Normalize(input));
    }
}
