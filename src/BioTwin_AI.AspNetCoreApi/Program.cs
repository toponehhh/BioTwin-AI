using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.AspNetCoreApi.Application.Chat;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Export;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.AspNetCoreApi.Application.Refinement;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using QuestPDF.Infrastructure;
using Serilog;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

builder.Logging.ClearProviders();
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// Add services to the container.

var defaultDbPath = Path.Combine(builder.Environment.ContentRootPath, "database", "biotwin-api.db");

builder.Services.AddDbContext<BioTwinApiDbContext>(options =>
{
    var connectionString = ResolveSqliteConnectionString(
        builder.Configuration.GetConnectionString("BioTwinApi"),
        builder.Environment.ContentRootPath,
        defaultDbPath);
    options.UseSqlite(connectionString);
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("all2md", client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("All2MD:TimeoutSeconds", 600));
});
builder.Services.AddSingleton<IChatClient>(CreateChatClient);
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:5193", "https://localhost:7193"];

        policy.WithOrigins(origins)
            .SetIsOriginAllowed(origin => origins.Contains(origin, StringComparer.OrdinalIgnoreCase)
                || (builder.Environment.IsDevelopment() && IsLocalDevelopmentOrigin(origin)))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BioTwin_AI.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IExternalProviderCatalog, ExternalProviderCatalog>();
builder.Services.AddScoped<ISessionResponseFactory, SessionResponseFactory>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<ILlmChatService, LlmChatService>();
builder.Services.AddSingleton<HashingEmbeddingService>();
builder.Services.AddSingleton<BgeM3OnnxEmbeddingService>();
builder.Services.AddSingleton<IEmbeddingService>(provider =>
{
    var environment = provider.GetRequiredService<IHostEnvironment>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<Program>>();
    var providerName = configuration["Embedding:Provider"] ?? "BgeM3Onnx";

    if (string.Equals(providerName, "BgeM3Onnx", StringComparison.OrdinalIgnoreCase) &&
        BgeM3OnnxEmbeddingService.CanLoad(environment, configuration))
    {
        try
        {
            return provider.GetRequiredService<BgeM3OnnxEmbeddingService>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BGE-M3 ONNX model files were found but could not be loaded. Falling back to hashing embeddings.");
        }
    }

    logger.LogWarning("BGE-M3 ONNX model files were not found or disabled. Falling back to hashing embeddings.");
    return provider.GetRequiredService<HashingEmbeddingService>();
});
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddScoped<IRagSearchService, RagSearchService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IResumePdfService, ResumePdfService>();
builder.Services.AddScoped<IResumeRefinementService, ResumeRefinementService>();

var app = builder.Build();

app.Logger.LogInformation("BioTwin AI API initialization started in {EnvironmentName}.", app.Environment.EnvironmentName);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BioTwinApiDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await EnsureUserProfileColumnsAsync(dbContext);
}

app.Logger.LogInformation("BioTwin AI API startup initialization completed.");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("BlazorClient");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation(
        "BioTwin AI API started successfully in {EnvironmentName}. Listening on {Urls}",
        app.Environment.EnvironmentName,
        string.Join(", ", app.Urls));
});

app.Run();

static bool IsLocalDevelopmentOrigin(string origin)
{
    return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase))
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

static string ResolveSqliteConnectionString(string? configuredConnectionString, string contentRootPath, string defaultDbPath)
{
    var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
        ? new SqliteConnectionStringBuilder { DataSource = defaultDbPath }.ToString()
        : configuredConnectionString;

    var builder = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrWhiteSpace(builder.DataSource)
        && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
        && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        && !Path.IsPathRooted(builder.DataSource))
    {
        builder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
    }

    var directory = Path.GetDirectoryName(builder.DataSource);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    return builder.ToString();
}

static IChatClient CreateChatClient(IServiceProvider services)
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var model = configuration["LLM:Model"] ?? "openrouter/free";
    var credential = new ApiKeyCredential(GetApiKey(configuration));
    var chatClient = new ChatClient(
        model,
        credential,
        new OpenAIClientOptions { Endpoint = GetOpenAiCompatibleEndpoint(configuration) });

    return chatClient.AsIChatClient();
}

static Uri GetOpenAiCompatibleEndpoint(IConfiguration configuration)
{
    var configured = configuration["LLM:BaseUrl"];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return new Uri("https://openrouter.ai/api/v1");
    }

    var trimmed = configured.TrimEnd('/');
    if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
    {
        trimmed += "/v1";
    }

    return new Uri(trimmed);
}

static string GetApiKey(IConfiguration configuration)
{
    var apiKey = FirstNonBlank(
        configuration["OpenRouter:ApiKey"],
        configuration["LLM:ApiKey"],
        Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));

    if (apiKey is null)
    {
        throw new InvalidOperationException("OpenRouter API key is missing. Set OpenRouter:ApiKey, LLM:ApiKey, or OPENROUTER_API_KEY.");
    }

    return apiKey;
}

static string? FirstNonBlank(params string?[] values)
{
    return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

static async Task EnsureUserProfileColumnsAsync(BioTwinApiDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var userColumns = await GetTableColumnsAsync(connection, "UserAccounts");
    if (!userColumns.Contains("Nickname"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE UserAccounts ADD COLUMN Nickname TEXT NOT NULL DEFAULT ''");
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE UserAccounts SET Nickname = Username WHERE Nickname IS NULL OR trim(Nickname) = ''");
    }

    if (!userColumns.Contains("Avatar"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE UserAccounts ADD COLUMN Avatar TEXT NOT NULL DEFAULT '🧑‍💻'");
        var legacyAvatarColumn = "Avatar" + "Emoji";
        if (userColumns.Contains(legacyAvatarColumn))
        {
            var legacyAvatarUpdateSql = string.Concat(
                "UPDATE UserAccounts SET Avatar = ",
                legacyAvatarColumn,
                " WHERE ",
                legacyAvatarColumn,
                " IS NOT NULL AND trim(",
                legacyAvatarColumn,
                ") <> ''");
            await dbContext.Database.ExecuteSqlRawAsync(legacyAvatarUpdateSql);
        }
    }

    await EnsureColumnAsync(dbContext, userColumns, "UpdatedAt", "ALTER TABLE UserAccounts ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await EnsureColumnAsync(dbContext, userColumns, "IsDeleted", "ALTER TABLE UserAccounts ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0");
    await EnsureColumnAsync(dbContext, userColumns, "DeletedAt", "ALTER TABLE UserAccounts ADD COLUMN DeletedAt TEXT NULL");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE UserAccounts SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01T00:00:00.000Z'");

    var externalIdentityColumns = await GetTableColumnsAsync(connection, "UserExternalIdentities");
    await EnsureColumnAsync(dbContext, externalIdentityColumns, "CreatedAt", "ALTER TABLE UserExternalIdentities ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await EnsureColumnAsync(dbContext, externalIdentityColumns, "UpdatedAt", "ALTER TABLE UserExternalIdentities ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE UserExternalIdentities SET CreatedAt = LinkedAt WHERE CreatedAt = '1970-01-01T00:00:00.000Z'");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE UserExternalIdentities SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01T00:00:00.000Z'");

    var resumeEntryColumns = await GetTableColumnsAsync(connection, "ResumeEntries");
    await EnsureColumnAsync(dbContext, resumeEntryColumns, "UpdatedAt", "ALTER TABLE ResumeEntries ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE ResumeEntries SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01T00:00:00.000Z'");

    var resumeSectionColumns = await GetTableColumnsAsync(connection, "ResumeSections");
    await EnsureColumnAsync(dbContext, resumeSectionColumns, "UpdatedAt", "ALTER TABLE ResumeSections ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE ResumeSections SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01T00:00:00.000Z'");

    var resumeSectionVectorColumns = await GetTableColumnsAsync(connection, "ResumeSectionVectors");
    await EnsureColumnAsync(dbContext, resumeSectionVectorColumns, "UpdatedAt", "ALTER TABLE ResumeSectionVectors ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.000Z'");
    await dbContext.Database.ExecuteSqlRawAsync("UPDATE ResumeSectionVectors SET UpdatedAt = CreatedAt WHERE UpdatedAt = '1970-01-01T00:00:00.000Z'");
}

static async Task<HashSet<string>> GetTableColumnsAsync(System.Data.Common.DbConnection connection, string tableName)
{
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = $"PRAGMA table_info('{tableName}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
    }

    return columns;
}

static async Task EnsureColumnAsync(
    BioTwinApiDbContext dbContext,
    HashSet<string> columns,
    string columnName,
    string alterSql)
{
    if (!columns.Contains(columnName))
    {
        await dbContext.Database.ExecuteSqlRawAsync(alterSql);
        columns.Add(columnName);
    }
}
