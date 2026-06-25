using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BioTwin_AI.BlazorClient;
using BioTwin_AI.BlazorClient.Services.Api;
using BioTwin_AI.BlazorClient.Services;
using BioTwin_AI.BlazorClient.Services.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
var baseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
    ? builder.HostEnvironment.BaseAddress
    : apiBaseUrl;
var apiHttpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddProvider(new RemoteClientLoggerProvider(apiHttpClient, "api/client-logs", LogLevel.Information));

builder.Services.AddFluentUIComponents();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<AuthModalState>();
builder.Services.AddScoped<SessionState>();
builder.Services.AddScoped(_ => apiHttpClient);
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<IChatApiClient, ChatApiClient>();
builder.Services.AddScoped<IResumeApiClient, ResumeApiClient>();
builder.Services.AddScoped<ISettingsApiClient, SettingsApiClient>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BioTwin_AI.BlazorClient.Startup");
logger.LogInformation(
    "BioTwin AI Blazor client started successfully in {EnvironmentName}. Base address: {BaseAddress}",
    builder.HostEnvironment.Environment,
    builder.HostEnvironment.BaseAddress);

await host.RunAsync();
