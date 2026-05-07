using BioTwin_AI.Components;
using BioTwin_AI.Data;
using BioTwin_AI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SQLite DbContext
var dbPath = Path.Combine(AppContext.BaseDirectory, "biotwin.db");
builder.Services.AddDbContext<BioTwinDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
);

// Configure RAG Service
builder.Services.AddScoped<RagService>();

// Configure Agent Service
builder.Services.AddScoped<AgentService>();

// Configure Resume Upload Service
builder.Services.AddScoped<ResumeUploadService>();

// Configure HTTP client for All2MD service
builder.Services.AddHttpClient<ResumeUploadService>();

// Add logging
builder.Logging.AddConsole();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BioTwinDbContext>();
    dbContext.Database.EnsureCreated();
}

// Initialize RAG system
using (var scope = app.Services.CreateScope())
{
    var ragService = scope.ServiceProvider.GetRequiredService<RagService>();
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
