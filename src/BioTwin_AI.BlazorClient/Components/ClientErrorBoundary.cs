using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace BioTwin_AI.BlazorClient.Components;

public sealed class ClientErrorBoundary : ErrorBoundary, IDisposable
{
    [Inject]
    public ILogger<ClientErrorBoundary> Logger { get; set; } = null!;

    [Inject]
    public NavigationManager Navigation { get; set; } = null!;

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += HandleLocationChanged;
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Unhandled Blazor component exception");
        return Task.CompletedTask;
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (CurrentException is not null)
        {
            _ = InvokeAsync(Recover);
        }
    }

    public void Dispose()
    {
        Navigation.LocationChanged -= HandleLocationChanged;
    }
}
