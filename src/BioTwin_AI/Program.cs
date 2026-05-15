using BioTwin_AI.Components;
using BioTwin_AI.Data;
using BioTwin_AI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();

// Configure SQLite DbContext
var dbDirectory = Path.Combine(builder.Environment.ContentRootPath, "database");
Directory.CreateDirectory(dbDirectory);
var dbPath = Path.Combine(dbDirectory, "biotwin.db");
builder.Services.AddDbContext<BioTwinDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
);

// Configure RAG Service
builder.Services.AddScoped<IRagService, RagService>();

// Configure Microsoft.Extensions.AI chat and embedding clients
builder.Services.AddBioTwinAiClients(builder.Configuration);

// Configure Embedding Service
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<IEmbeddingService>(provider =>
    provider.GetRequiredService<EmbeddingService>());

// Configure lightweight auth/session services
builder.Services.AddScoped<CurrentUserSession>();
builder.Services.AddScoped<AuthService>();

// Configure Agent Service
builder.Services.AddScoped<AgentService>();

// Configure resume Markdown refinement service
builder.Services.AddScoped<ResumeMarkdownRefinementService>();
builder.Services.AddScoped<ResumePdfExportService>();

// Configure HTTP client for All2MD service
builder.Services.AddHttpClient<ResumeUploadService>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var timeoutSeconds = configuration.GetValue("All2MD:TimeoutSeconds", 600);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

var supportedCultureNames = new[] { "en", "zh-CN" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultureNames)
    .AddSupportedUICultures(supportedCultureNames));

// Initialize RAG system
using (var scope = app.Services.CreateScope())
{
    var ragService = scope.ServiceProvider.GetRequiredService<IRagService>();
    await ragService.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/culture/set", (HttpContext httpContext, string culture, string? redirectUri) =>
{
    var selectedCulture = supportedCultureNames.FirstOrDefault(
        supportedCulture => string.Equals(supportedCulture, culture, StringComparison.OrdinalIgnoreCase)) ?? "en";
    var returnUri = string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri;
    if (!Uri.IsWellFormedUriString(returnUri, UriKind.Relative) ||
        returnUri.StartsWith("//", StringComparison.Ordinal))
    {
        returnUri = "/";
    }

    httpContext.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selectedCulture)),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax
        });

    return Results.LocalRedirect(returnUri);
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
