using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Infrastructure;

public sealed class SqliteSchemaMetadataTests
{
    [Fact]
    public async Task Startup_schema_validator_accepts_current_sqlite_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new BioTwinApiDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        await using var validationContext = new BioTwinApiDbContext(options);
        await DatabaseSchemaValidator.ValidateAsync(validationContext, NullLogger.Instance);
    }

    [Fact]
    public async Task Migration_scripts_can_be_executed_more_than_once()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var migrationsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Infrastructure", "Data", "Migrations"));
        var migrationScripts = Directory
            .GetFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var migrationScript in migrationScripts)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = await File.ReadAllTextAsync(migrationScript);
                await command.ExecuteNonQueryAsync();
            }
        }

        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var validationContext = new BioTwinApiDbContext(options);

        await DatabaseSchemaValidator.ValidateAsync(validationContext, NullLogger.Instance);
    }

    [Fact]
    public async Task Migration_scripts_can_run_when_sql_client_wraps_each_script_in_transaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var migrationsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Infrastructure", "Data", "Migrations"));
        var migrationScripts = Directory
            .GetFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var migrationScript in migrationScripts)
        {
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = await File.ReadAllTextAsync(migrationScript);
            await command.ExecuteNonQueryAsync();
            transaction.Commit();
        }

        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var validationContext = new BioTwinApiDbContext(options);

        await DatabaseSchemaValidator.ValidateAsync(validationContext, NullLogger.Instance);
    }

    [Fact]
    public async Task Candidate_profile_followup_scripts_do_not_require_profile_columns_to_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE UserAccounts (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );

                INSERT INTO UserAccounts (Username, PasswordHash, Role, CreatedAt)
                VALUES ('candidate', 'hash', 'Candidate', '2026-07-07T00:00:00.000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var migrationsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Infrastructure", "Data", "Migrations"));

        foreach (var migrationFileName in new[]
        {
            "004-add-candidate-profile-sharing.sql",
            "005-complete-candidate-profile-sharing.sql"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(migrationsDirectory, migrationFileName));
            await command.ExecuteNonQueryAsync();
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM UserRoles";
        var roleCount = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(1L, roleCount);
    }
}
