namespace BioTwin_AI.AspNetCoreApi.Tests.Architecture;

public sealed class PhaseImplementationTests
{
    [Fact]
    public void Controllers_do_not_return_not_implemented_scaffold_responses()
    {
        var controllerDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Controllers"));

        var controllerText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(controllerDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Status501NotImplemented", controllerText, StringComparison.Ordinal);
        Assert.DoesNotContain("501", controllerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_program_registers_milestone_one_infrastructure()
    {
        var programPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Program.cs"));
        var programText = File.ReadAllText(programPath);

        Assert.Contains("AddDbContext", programText, StringComparison.Ordinal);
        Assert.Contains("AddCors", programText, StringComparison.Ordinal);
        Assert.Contains("SetIsOriginAllowed", programText, StringComparison.Ordinal);
        Assert.Contains("IsLocalDevelopmentOrigin", programText, StringComparison.Ordinal);
        Assert.Contains("UseSerilog", programText, StringComparison.Ordinal);
        Assert.Contains("EnsureCreatedAsync", programText, StringComparison.Ordinal);
        Assert.Contains("UseAuthentication", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_registers_openrouter_compatible_llm_client()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var projectText = File.ReadAllText(Path.Combine(apiDirectory, "BioTwin_AI.AspNetCoreApi.csproj"));
        var programText = File.ReadAllText(Path.Combine(apiDirectory, "Program.cs"));
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));

        Assert.Contains("Microsoft.Extensions.AI.OpenAI", projectText, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ILlmChatService", programText, StringComparison.Ordinal);
        Assert.Contains("\"LLM\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"BaseUrl\": \"https://openrouter.ai/api/v1\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"Model\": \"openrouter/free\"", appsettingsText, StringComparison.Ordinal);
        Assert.DoesNotContain("not-needed", programText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Api_logging_defaults_to_information_visibility_and_explicit_startup_success()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var programText = File.ReadAllText(Path.Combine(apiDirectory, "Program.cs"));
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));

        Assert.Contains("\"Default\": \"Information\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.AspNetCore\": \"Information\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft\": \"Information\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.Hosting.Lifetime\": \"Information\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"System\": \"Information\"", appsettingsText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Trace\"", appsettingsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Verbose\"", appsettingsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationStarted.Register", programText, StringComparison.Ordinal);
        Assert.Contains("started successfully", programText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app.Logger.LogInformation", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_accepts_client_logs_through_dedicated_controller()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var controllerText = File.ReadAllText(Path.Combine(apiDirectory, "Controllers", "ClientLogsController.cs"));

        Assert.Contains("[Route(\"api/client-logs\")]", controllerText, StringComparison.Ordinal);
        Assert.Contains("ClientLogEntryRequest", controllerText, StringComparison.Ordinal);
        Assert.Contains("LogLevel.Information", controllerText, StringComparison.Ordinal);
        Assert.Contains("LogLevel.Debug", controllerText, StringComparison.Ordinal);
        Assert.Contains("return Accepted();", controllerText, StringComparison.Ordinal);
        Assert.Contains("logger.Log", controllerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_and_resume_refinement_use_llm_service()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var chatServiceText = File.ReadAllText(Path.Combine(apiDirectory, "Application", "Chat", "ChatService.cs"));
        var refinementServiceText = File.ReadAllText(Path.Combine(apiDirectory, "Application", "Refinement", "ResumeRefinementService.cs"));
        var refinementControllerText = File.ReadAllText(Path.Combine(apiDirectory, "Controllers", "ResumeRefinementController.cs"));

        Assert.Contains("ILlmChatService", chatServiceText, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync", chatServiceText, StringComparison.Ordinal);
        Assert.Contains("StreamAsync", chatServiceText, StringComparison.Ordinal);
        Assert.Contains("ILlmChatService", refinementServiceText, StringComparison.Ordinal);
        Assert.Contains("RefineAsync", refinementServiceText, StringComparison.Ordinal);
        Assert.Contains("Task<ActionResult<RefineMarkdownResponse>>", refinementControllerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_configuration_declares_local_database_and_development_all2md()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));
        var developmentText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.Development.json"));

        Assert.Contains("\"BioTwinApi\": \"Data Source=database/biotwin-api.db\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"ApiUrl\": \"http://localhost:8000\"", developmentText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_persists_user_profile_fields_and_exposes_profile_update_endpoint()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var userAccountText = File.ReadAllText(Path.Combine(apiDirectory, "Infrastructure", "Data", "Entities", "UserAccount.cs"));
        var dbContextText = File.ReadAllText(Path.Combine(apiDirectory, "Infrastructure", "Data", "BioTwinApiDbContext.cs"));
        var authControllerText = File.ReadAllText(Path.Combine(apiDirectory, "Controllers", "AuthController.cs"));

        Assert.Contains("Nickname", userAccountText, StringComparison.Ordinal);
        Assert.Contains("Avatar", userAccountText, StringComparison.Ordinal);
        Assert.Contains("IsDeleted", userAccountText, StringComparison.Ordinal);
        Assert.Contains("DeletedAt", userAccountText, StringComparison.Ordinal);
        Assert.DoesNotContain("AvatarEmoji", userAccountText, StringComparison.Ordinal);
        Assert.Contains("user => user.Nickname", dbContextText, StringComparison.Ordinal);
        Assert.Contains("user => user.Avatar", dbContextText, StringComparison.Ordinal);
        Assert.Contains("UpdateProfile", authControllerText, StringComparison.Ordinal);
        Assert.Contains("UpdateProfileRequest", authControllerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_entities_use_created_and_updated_audit_columns()
    {
        var entityDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi", "Infrastructure", "Data", "Entities"));
        var entityFiles = new[]
        {
            "UserAccount.cs",
            "UserExternalIdentity.cs",
            "ResumeEntry.cs",
            "ResumeSection.cs",
            "ResumeSectionVector.cs"
        };

        foreach (var entityFile in entityFiles)
        {
            var text = File.ReadAllText(Path.Combine(entityDirectory, entityFile));
            Assert.Contains("CreatedAt", text, StringComparison.Ordinal);
            Assert.Contains("UpdatedAt", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Required_user_identity_relationship_uses_matching_soft_delete_query_filters()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var dbContextText = File.ReadAllText(Path.Combine(apiDirectory, "Infrastructure", "Data", "BioTwinApiDbContext.cs"));

        Assert.Contains("entity.HasQueryFilter(user => !user.IsDeleted);", dbContextText, StringComparison.Ordinal);
        Assert.Contains("entity.HasQueryFilter(identity => !identity.User!.IsDeleted);", dbContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_database_changes_are_backed_by_sql_migration_scripts()
    {
        var migrationsDirectory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "BioTwin_AI.AspNetCoreApi",
                "Infrastructure",
                "Data",
                "Migrations"));

        Assert.True(Directory.Exists(migrationsDirectory), "Database SQL migrations directory is required.");

        var initialSchemaPath = Path.Combine(migrationsDirectory, "001-initial-schema.sql");
        var profileMigrationPath = Path.Combine(migrationsDirectory, "002-add-user-profile-fields.sql");
        Assert.True(File.Exists(initialSchemaPath), "Initial schema SQL script is required.");
        Assert.True(File.Exists(profileMigrationPath), "User profile SQL migration script is required.");

        var initialSchemaSql = File.ReadAllText(initialSchemaPath);
        var profileMigrationSql = File.ReadAllText(profileMigrationPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS UserAccounts", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS UserExternalIdentities", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS ResumeEntries", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS ResumeSections", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS ResumeSectionVectors", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_Username", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOREIGN KEY", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AvatarEmoji", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avatar TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedAt TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsDeleted INTEGER NOT NULL DEFAULT 0", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeletedAt TEXT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ALTER TABLE UserAccounts ADD COLUMN Nickname", profileMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE UserAccounts ADD COLUMN Avatar", profileMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AvatarEmoji", profileMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE UserAccounts", profileMigrationSql, StringComparison.OrdinalIgnoreCase);

        var auditMigrationPath = Path.Combine(migrationsDirectory, "003-add-audit-and-user-soft-delete-fields.sql");
        Assert.True(File.Exists(auditMigrationPath), "Audit and user soft delete SQL migration script is required.");
        var auditMigrationSql = File.ReadAllText(auditMigrationPath);

        Assert.Contains("ALTER TABLE UserAccounts ADD COLUMN UpdatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE UserAccounts ADD COLUMN IsDeleted", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE UserAccounts ADD COLUMN DeletedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE UserExternalIdentities ADD COLUMN CreatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE UserExternalIdentities ADD COLUMN UpdatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE ResumeEntries ADD COLUMN UpdatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE ResumeSections ADD COLUMN UpdatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER TABLE ResumeSectionVectors ADD COLUMN UpdatedAt", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
    }
}
