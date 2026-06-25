using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.AspNetCoreApi.Application.Chat;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Export;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.AspNetCoreApi.Application.Refinement;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BioTwinApiDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

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
