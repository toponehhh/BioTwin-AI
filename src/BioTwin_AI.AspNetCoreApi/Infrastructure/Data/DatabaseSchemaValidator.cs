using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Data;

public static class DatabaseSchemaValidator
{
    public static async Task ValidateAsync(
        BioTwinApiDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var databaseTarget = DescribeDatabaseTarget(dbContext.Database.GetConnectionString());
        EnsureSqliteDatabaseFileExists(dbContext.Database.GetConnectionString(), databaseTarget);

        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Database schema is out of date. The API cannot connect to the configured database '{databaseTarget}'. Run the SQL migration scripts in src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Migrations manually, then restart the API.");
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var tables = await ReadTableNamesAsync(connection, cancellationToken);
        var indexes = await ReadIndexNamesAsync(connection, cancellationToken);
        var missingItems = new List<string>();

        foreach (var requiredTable in RequiredTables())
        {
            if (!tables.Contains(requiredTable.Key))
            {
                missingItems.Add(requiredTable.Key);
                continue;
            }

            var existingColumns = await ReadColumnNamesAsync(connection, requiredTable.Key, cancellationToken);
            foreach (var requiredColumn in requiredTable.Value)
            {
                if (!existingColumns.Contains(requiredColumn))
                {
                    missingItems.Add($"{requiredTable.Key}.{requiredColumn}");
                }
            }
        }

        foreach (var requiredIndex in RequiredIndexes())
        {
            if (!indexes.Contains(requiredIndex))
            {
                missingItems.Add(requiredIndex);
            }
        }

        if (missingItems.Count > 0)
        {
            var missingSchema = string.Join(", ", missingItems);
            logger.LogCritical(
                "Database schema validation failed for {DatabaseTarget}. Missing schema items: {MissingSchemaItems}",
                databaseTarget,
                missingSchema);
            throw new InvalidOperationException($"Database schema is out of date for '{databaseTarget}'. Run the SQL migration scripts in src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Migrations manually, then restart the API. Missing: {missingSchema}");
        }

        logger.LogInformation("Database schema validation passed for {DatabaseTarget}.", databaseTarget);
    }

    private static void EnsureSqliteDatabaseFileExists(string? connectionString, string databaseTarget)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(builder.DataSource))
        {
            throw new InvalidOperationException($"Database schema is out of date. Database file was not found at '{databaseTarget}'. Run the SQL migration scripts in src/BioTwin_AI.AspNetCoreApi/Infrastructure/Data/Migrations manually, then restart the API.");
        }
    }

    private static string DescribeDatabaseTarget(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "<empty connection string>";
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        return string.IsNullOrWhiteSpace(builder.DataSource)
            ? connectionString
            : builder.DataSource;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<HashSet<string>> ReadIndexNamesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%'";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static IReadOnlyDictionary<string, string[]> RequiredTables()
    {
        return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UserAccounts"] =
            [
                "Id",
                "Username",
                "Nickname",
                "Avatar",
                "PasswordHash",
                "Role",
                "ProfileHash",
                "ProfileHashUpdatedAt",
                "CandidateProfileVersion",
                "IsProfilePublic",
                "IsDefaultCandidate",
                "CreatedAt",
                "UpdatedAt",
                "IsDeleted",
                "DeletedAt"
            ],
            ["UserExternalIdentities"] =
            [
                "Id",
                "UserId",
                "Provider",
                "ProviderUserId",
                "ProviderEmail",
                "ProviderDisplayName",
                "ProviderAvatarUrl",
                "CreatedAt",
                "UpdatedAt"
            ],
            ["UserRoles"] =
            [
                "Id",
                "UserId",
                "Role",
                "CreatedAt"
            ],
            ["ResumeEntries"] =
            [
                "Id",
                "TenantId",
                "Language",
                "Title",
                "SourceFileName",
                "SourceContentType",
                "SourceFileSize",
                "SourceFileContent",
                "SourceFileHash",
                "CreatedAt",
                "UpdatedAt"
            ],
            ["ResumeSections"] =
            [
                "Id",
                "ResumeEntryId",
                "TenantId",
                "ParentSectionId",
                "HeadingLevel",
                "Title",
                "Content",
                "SortOrder",
                "CreatedAt",
                "UpdatedAt"
            ],
            ["ResumeSectionVectors"] =
            [
                "Id",
                "ResumeSectionId",
                "TenantId",
                "ResumeTitle",
                "SectionTitle",
                "Content",
                "EmbeddingPayload",
                "CreatedAt",
                "UpdatedAt"
            ],
            ["CandidateProfileInfos"] =
            [
                "Id",
                "UserId",
                "InfoType",
                "Version",
                "JsonData",
                "Source",
                "SourceResumeVersion",
                "BasedOnInfoId",
                "ModelName",
                "PromptVersion",
                "IsCurrent",
                "CreatedAt",
                "UpdatedAt",
                "CreatedByUserId"
            ]
        };
    }

    private static string[] RequiredIndexes()
    {
        return
        [
            "IX_UserAccounts_Username",
            "IX_UserExternalIdentities_Provider_ProviderUserId",
            "IX_UserAccounts_ProfileHash",
            "IX_UserAccounts_DefaultCandidate",
            "IX_UserRoles_UserId_Role",
            "IX_ResumeEntries_TenantId_Language",
            "IX_CandidateProfileInfos_UserId_InfoType_Version",
            "IX_CandidateProfileInfos_UserId_InfoType_IsCurrent"
        ];
    }
}
