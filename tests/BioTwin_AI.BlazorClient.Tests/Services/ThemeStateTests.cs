using BioTwin_AI.BlazorClient.Services;

namespace BioTwin_AI.BlazorClient.Tests.Services;

public class ThemeStateTests
{
    [Fact]
    public void ThemeState_defaults_to_dark_and_can_switch_theme()
    {
        var state = new ThemeState();

        Assert.Equal(AppTheme.Dark, state.CurrentTheme);

        state.SetTheme(AppTheme.Light);

        Assert.Equal(AppTheme.Light, state.CurrentTheme);
    }
}
