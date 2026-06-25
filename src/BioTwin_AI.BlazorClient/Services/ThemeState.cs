using Microsoft.JSInterop;

namespace BioTwin_AI.BlazorClient.Services;

public sealed class ThemeState
{
    private const string StorageKey = "biotwin.theme";
    private readonly IJSRuntime? _jsRuntime;

    public ThemeState()
    {
    }

    public ThemeState(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_jsRuntime is null)
        {
            return;
        }

        var value = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed))
        {
            CurrentTheme = parsed;
        }
    }

    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        Changed?.Invoke();
        _ = PersistAsync(theme);
    }

    private async Task PersistAsync(AppTheme theme)
    {
        if (_jsRuntime is null)
        {
            return;
        }

        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, theme.ToString());
    }
}
