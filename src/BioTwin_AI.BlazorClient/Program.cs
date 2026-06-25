using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BioTwin_AI.BlazorClient;
using BioTwin_AI.BlazorClient.Services.Api;
using BioTwin_AI.BlazorClient.Services;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<AuthModalState>();
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped(sp =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
    var baseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
        ? builder.HostEnvironment.BaseAddress
        : apiBaseUrl;
    return new HttpClient { BaseAddress = new Uri(baseAddress) };
});
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<IChatApiClient, ChatApiClient>();
builder.Services.AddScoped<IResumeApiClient, ResumeApiClient>();
builder.Services.AddScoped<ISettingsApiClient, SettingsApiClient>();

await builder.Build().RunAsync();
