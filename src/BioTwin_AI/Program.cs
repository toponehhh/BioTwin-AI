using BioTwin_AI.Components;
using BioTwin_AI.Data;
using BioTwin_AI.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SQLite DbContext
var dbDirectory = Path.Combine(builder.Environment.ContentRootPath, "database");
Directory.CreateDirectory(dbDirectory);
var dbPath = Path.Combine(dbDirectory, "biotwin.db");
builder.Services.AddDbContext<BioTwinDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
);

// Configure RAG Service
builder.Services.AddScoped<IRagService, RagService>();

// Configure Embedding Service
builder.Services.AddHttpClient<EmbeddingService>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var timeoutSeconds = configuration.GetValue("LLM:EmbeddingTimeoutSeconds", 300);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddScoped<IEmbeddingService>(provider =>
    provider.GetRequiredService<EmbeddingService>());

// Configure lightweight auth/session services
builder.Services.AddScoped<CurrentUserSession>();
builder.Services.AddScoped<AuthService>();

// Configure Agent Service
builder.Services.AddHttpClient<AgentService>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var timeoutSeconds = configuration.GetValue("LLM:ChatTimeoutSeconds", 300);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

// Configure resume Markdown refinement service
builder.Services.AddHttpClient<ResumeMarkdownRefinementService>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var timeoutSeconds = configuration.GetValue("ResumeMarkdownRefinement:TimeoutSeconds", 300);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
