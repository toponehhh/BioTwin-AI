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

// Configure HTTP client for All2MD service
builder.Services.AddHttpClient<ResumeUploadService>((provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var timeoutSeconds = configuration.GetValue("All2MD:TimeoutSeconds", 600);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});

// Add logging
builder.Logging.AddConsole();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BioTwinDbContext>();
    dbContext.Database.EnsureCreated();

    // Patch existing DB to the current resume/file + sections schema (DB-first friendly, idempotent).
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

    async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM pragma_table_info('{tableName}') WHERE name = $columnName;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$columnName";
        parameter.Value = columnName;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    var hasLegacyTitle = await ColumnExistsAsync("ResumeEntries", "Title");
    var hasLegacyContent = await ColumnExistsAsync("ResumeEntries", "Content");

    if (hasLegacyTitle && hasLegacyContent)
    {
        // Resume data can be discarded during this schema redesign. Keep user accounts,
        // but reset resume tables instead of carrying old section data forward.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
        await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS ResumeSections;");
        await dbContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS ResumeEntries;");
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
    }

    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ResumeEntries (
            Id INTEGER NOT NULL CONSTRAINT PK_ResumeEntries PRIMARY KEY AUTOINCREMENT,
            SourceFileName TEXT NOT NULL,
            SourceFileContent BLOB NULL,
            SourceContentType TEXT NULL,
            SourceFileSize INTEGER NULL,
            CreatedAt TEXT NOT NULL,
            TenantId TEXT NOT NULL DEFAULT 'default'
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId ON ResumeEntries(TenantId);");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeEntries_TenantId_CreatedAt ON ResumeEntries(TenantId, CreatedAt);");

    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ResumeSections (
            Id INTEGER NOT NULL CONSTRAINT PK_ResumeSections PRIMARY KEY AUTOINCREMENT,
            ResumeEntryId INTEGER NOT NULL,
            ParentSectionId INTEGER NULL,
            HeadingLevel INTEGER NOT NULL DEFAULT 2,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            TenantId TEXT NOT NULL DEFAULT 'default',
            VectorId TEXT NULL,
            CONSTRAINT FK_ResumeSections_ResumeEntries_ResumeEntryId FOREIGN KEY (ResumeEntryId) REFERENCES ResumeEntries (Id) ON DELETE CASCADE,
            CONSTRAINT FK_ResumeSections_ResumeSections_ParentSectionId FOREIGN KEY (ParentSectionId) REFERENCES ResumeSections (Id) ON DELETE SET NULL
        );
    ");
    if (!await ColumnExistsAsync("ResumeSections", "ParentSectionId"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ResumeSections ADD COLUMN ParentSectionId INTEGER NULL;");
    }

    if (!await ColumnExistsAsync("ResumeSections", "HeadingLevel"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ResumeSections ADD COLUMN HeadingLevel INTEGER NOT NULL DEFAULT 2;");
    }

    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeSections_TenantId ON ResumeSections(TenantId);");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeSections_TenantId_CreatedAt ON ResumeSections(TenantId, CreatedAt);");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeSections_ResumeEntryId_SortOrder ON ResumeSections(ResumeEntryId, SortOrder);");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ResumeSections_ParentSectionId ON ResumeSections(ParentSectionId);");

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
