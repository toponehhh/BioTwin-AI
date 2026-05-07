using BioTwin_AI.Components;
using BioTwin_AI.Data;
using BioTwin_AI.Services;
using Microsoft.EntityFrameworkCore;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

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

// Configure lightweight auth/session services
builder.Services.AddScoped<CurrentUserSession>();
builder.Services.AddScoped<AuthService>();

// Configure Agent Service
builder.Services.AddHttpClient<AgentService>();

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

    // Patch existing DB to multi-tenant schema (DB-first friendly, idempotent).
    await EnsureMultiTenantSchemaAsync(dbContext);
}

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

static async Task EnsureMultiTenantSchemaAsync(BioTwinDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var checkCommand = connection.CreateCommand();
    checkCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('ResumeEntries') WHERE name = 'TenantId';";
    var tenantColumnExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;

    if (!tenantColumnExists)
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ResumeEntries ADD COLUMN TenantId TEXT NOT NULL DEFAULT 'default';");
    }

    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId ON ResumeEntries(TenantId);");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_CreatedAt ON ResumeEntries(TenantId, CreatedAt);");

    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS UserAccounts (
            Id INTEGER NOT NULL CONSTRAINT PK_UserAccounts PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_Username ON UserAccounts(Username);");
}
