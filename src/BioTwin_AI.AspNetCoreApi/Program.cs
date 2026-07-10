using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.AspNetCoreApi.Application.Chat;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Export;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.AspNetCoreApi.Application.Refinement;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Serilog;

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

var backendProjectRoot = ResolveBackendProjectRoot(
    builder.Environment.ContentRootPath,
    AppContext.BaseDirectory,
    "BioTwin_AI.AspNetCoreApi.csproj");
var defaultDbPath = Path.Combine(backendProjectRoot, "database", "biotwin-api.db");

builder.Services.AddDbContext<BioTwinApiDbContext>(options =>
{
    var connectionString = ResolveSqliteConnectionString(
        builder.Configuration.GetConnectionString("BioTwinApi"),
        backendProjectRoot,
        defaultDbPath);
    options.UseSqlite(connectionString);
});

builder.Services.AddControllers(options => options.Filters.Add<ResumeApiExceptionFilter>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAiProviders(builder.Configuration);
builder.Services.AddHttpClient("all2md", client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("All2MD:TimeoutSeconds", 600));
});
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
builder.Services.AddScoped<IUserRoleService, UserRoleService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IProfileShareCodeGenerator, ProfileShareCodeGenerator>();
builder.Services.AddScoped<ICandidateProfileInfoService, CandidateProfileInfoService>();
builder.Services.AddScoped<ICandidateProfileExtractionService, CandidateProfileExtractionService>();
builder.Services.AddScoped<IPublicCandidateProfileService, PublicCandidateProfileService>();
builder.Services.AddSingleton<ILlmChatService, LlmChatService>();
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddScoped<IResumeStateTokenService, ResumeStateTokenService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IResumeOperationCoordinator, MemoryResumeOperationCoordinator>();
builder.Services.AddScoped<IResumeOperationService, ResumeOperationService>();
builder.Services.AddSingleton<IResumeConversionJobService, ResumeConversionJobService>();
builder.Services.AddScoped<ResumeImportJobProcessor>();
builder.Services.AddSingleton<IResumeImportJobService, ResumeImportJobService>();
builder.Services.AddScoped<IResumeWizardExtractionService, ResumeWizardExtractionService>();
builder.Services.AddScoped<IRagSearchService, RagSearchService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IResumePdfService, ResumePdfService>();
builder.Services.AddScoped<IResumeRefinementService, ResumeRefinementService>();

var app = builder.Build();

app.Logger.LogInformation("BioTwin AI API initialization started in {EnvironmentName}.", app.Environment.EnvironmentName);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BioTwinApiDbContext>();
    await DatabaseSchemaValidator.ValidateAsync(dbContext, app.Logger);
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

static string ResolveBackendProjectRoot(string contentRootPath, string baseDirectory, string projectFileName)
{
    foreach (var candidateRoot in new[] { baseDirectory, contentRootPath }.Where(path => !string.IsNullOrWhiteSpace(path)))
    {
        var projectRoot = FindAncestorContainingFile(candidateRoot, projectFileName);
        if (projectRoot is not null)
        {
            return projectRoot;
        }
    }

    return contentRootPath;
}
static string? FindAncestorContainingFile(string startPath, string fileName)
{
    var directory = Directory.Exists(startPath)
        ? new DirectoryInfo(startPath)
        : Directory.GetParent(startPath);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, fileName)))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

static string ResolveSqliteConnectionString(string? configuredConnectionString, string databaseRootPath, string defaultDbPath)
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
        builder.DataSource = Path.GetFullPath(Path.Combine(databaseRootPath, builder.DataSource));
    }

    var directory = Path.GetDirectoryName(builder.DataSource);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    return builder.ToString();
}
