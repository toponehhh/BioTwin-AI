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
        Assert.Contains("DatabaseSchemaValidator.ValidateAsync", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreatedAsync", programText, StringComparison.Ordinal);
        Assert.Contains("ResolveBackendProjectRoot", programText, StringComparison.Ordinal);
        Assert.Contains("BioTwin_AI.AspNetCoreApi.csproj", programText, StringComparison.Ordinal);
        Assert.Contains("AppContext.BaseDirectory", programText, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(builder.Environment.ContentRootPath, \"database\"", programText, StringComparison.Ordinal);
        Assert.Contains("UseAuthentication", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_registers_native_cloudflare_and_openrouter_fallback_clients()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var projectText = File.ReadAllText(Path.Combine(apiDirectory, "BioTwin_AI.AspNetCoreApi.csproj"));
        var programText = File.ReadAllText(Path.Combine(apiDirectory, "Program.cs"));
        var aiRegistrationText = File.ReadAllText(Path.Combine(
            apiDirectory,
            "Infrastructure",
            "Ai",
            "AiServiceCollectionExtensions.cs"));
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));
        var developmentText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.Development.json"));

        Assert.Contains("Microsoft.Extensions.AI.OpenAI", projectText, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ILlmChatService", programText, StringComparison.Ordinal);
        Assert.Contains("\"LLM\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"PrimaryProvider\": \"CloudflareWorkersAI\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"FallbackProvider\": \"OpenRouter\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"BaseUrl\": \"https://openrouter.ai/api/v1\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"ChatModel\": \"openrouter/free\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<CloudflareChatClient>", aiRegistrationText, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<CloudflareChatClient>", aiRegistrationText, StringComparison.Ordinal);
        Assert.Contains("CreateChatClient", aiRegistrationText, StringComparison.Ordinal);
        Assert.Contains("NetworkTimeout = networkTimeout", aiRegistrationText, StringComparison.Ordinal);
        Assert.Contains("\"RequestTimeoutSeconds\": 180", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"ExtractionTimeoutSeconds\": 600", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"RequestTimeoutSeconds\": 600", developmentText, StringComparison.Ordinal);
        Assert.Contains("\"ExtractionTimeoutSeconds\": 600", developmentText, StringComparison.Ordinal);
        Assert.DoesNotContain("not-needed", programText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Api_project_overrides_vulnerable_openapi_transitive_dependency()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var projectText = File.ReadAllText(Path.Combine(apiDirectory, "BioTwin_AI.AspNetCoreApi.csproj"));

        Assert.Contains("PackageReference Include=\"Microsoft.AspNetCore.OpenApi\" Version=\"10.0.9\" ExcludeAssets=\"analyzers\" PrivateAssets=\"all\"", projectText, StringComparison.Ordinal);
        Assert.Contains("PackageReference Include=\"Microsoft.OpenApi\" Version=\"3.7.0\" PrivateAssets=\"all\"", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"Microsoft.OpenApi\" Version=\"2.0.0\"", projectText, StringComparison.Ordinal);
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
        Assert.Contains("ShouldLog", controllerText, StringComparison.Ordinal);
        Assert.Contains("StartupCategory", controllerText, StringComparison.Ordinal);
        Assert.Contains("MaxCategoryLength", controllerText, StringComparison.Ordinal);
        Assert.Contains("MaxMessageLength", controllerText, StringComparison.Ordinal);
        Assert.Contains("MaxExceptionLength", controllerText, StringComparison.Ordinal);
        Assert.Contains("SanitizeUrl", controllerText, StringComparison.Ordinal);
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
    public void Api_embedding_configuration_uses_shared_solution_llm_directory()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var apiDirectory = Path.Combine(solutionRoot, "src", "BioTwin_AI.AspNetCoreApi");
        var appsettingsText = File.ReadAllText(Path.Combine(apiDirectory, "appsettings.json"));

        Assert.Contains("\"ModelDirectory\": \"../../LLM/bge_m3\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"ModelPath\": \"bge_m3_model.onnx\"", appsettingsText, StringComparison.Ordinal);
        Assert.Contains("\"TokenizerPath\": \"bge_m3_tokenizer.onnx\"", appsettingsText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(solutionRoot, "LLM", "bge_m3", "bge_m3_model.onnx")));
        Assert.True(File.Exists(Path.Combine(solutionRoot, "LLM", "bge_m3", "bge_m3_tokenizer.onnx")));
    }

    [Fact]
    public void Backend_projects_link_shared_llm_assets_for_publish()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var legacyProjectText = File.ReadAllText(Path.Combine(solutionRoot, "src", "BioTwin_AI", "BioTwin_AI.csproj"));
        var apiProjectText = File.ReadAllText(Path.Combine(solutionRoot, "src", "BioTwin_AI.AspNetCoreApi", "BioTwin_AI.AspNetCoreApi.csproj"));

        foreach (var projectText in new[] { legacyProjectText, apiProjectText })
        {
            Assert.Contains("..\\..\\LLM\\README.md", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\download-bge-m3-onnx.ps1", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\download-bge-m3-onnx.sh", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\download-bge-reranker-v2-m3-onnx.ps1", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\download-bge-reranker-v2-m3-onnx.sh", projectText, StringComparison.Ordinal);
            Assert.Contains("Link=\"LLM\\%(Filename)%(Extension)\"", projectText, StringComparison.Ordinal);
            Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", projectText, StringComparison.Ordinal);
            Assert.Contains("IncludeLocalModels", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\bge_m3\\**\\*", projectText, StringComparison.Ordinal);
            Assert.Contains("..\\..\\LLM\\bge_rerank_v2\\**\\*", projectText, StringComparison.Ordinal);
            Assert.Contains("Condition=\"'$(IncludeLocalModels)' == 'true'\"", projectText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shared_llm_directory_has_download_scripts_for_embedding_and_rerank_models()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var llmDirectory = Path.Combine(solutionRoot, "LLM");
        var rerankPowerShell = File.ReadAllText(Path.Combine(llmDirectory, "download-bge-reranker-v2-m3-onnx.ps1"));
        var rerankBash = File.ReadAllText(Path.Combine(llmDirectory, "download-bge-reranker-v2-m3-onnx.sh"));
        var readme = File.ReadAllText(Path.Combine(llmDirectory, "README.md"));

        foreach (var scriptText in new[] { rerankPowerShell, rerankBash })
        {
            Assert.Contains("https://huggingface.co/kftof/bge-reranker-v2-m3-onnx-int8-avx2/resolve/main", scriptText, StringComparison.Ordinal);
            Assert.Contains("model.onnx", scriptText, StringComparison.Ordinal);
            Assert.Contains("tokenizer.json", scriptText, StringComparison.Ordinal);
            Assert.Contains("config.json", scriptText, StringComparison.Ordinal);
            Assert.Contains("special_tokens_map.json", scriptText, StringComparison.Ordinal);
            Assert.Contains("tokenizer_config.json", scriptText, StringComparison.Ordinal);
            Assert.Contains("bge_rerank_v2", scriptText, StringComparison.Ordinal);
        }

        Assert.Contains("download-bge-m3-onnx.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("download-bge-reranker-v2-m3-onnx.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("download-bge-reranker-v2-m3-onnx.sh", readme, StringComparison.Ordinal);
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
        Assert.Contains("entity.HasQueryFilter(role => !role.User!.IsDeleted);", dbContextText, StringComparison.Ordinal);
        Assert.Contains("entity.HasQueryFilter(info => !info.User!.IsDeleted);", dbContextText, StringComparison.Ordinal);
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
        var migrationSqlFiles = Directory.GetFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly);

        Assert.Contains("DROP TABLE IF EXISTS UserAccounts", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE UserAccounts", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE UserExternalIdentities", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE UserRoles", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE CandidateProfileInfos", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE ResumeEntries", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE ResumeSections", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE ResumeSectionVectors", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserAccounts_Username", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOREIGN KEY", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AvatarEmoji", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avatar TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProfileHash TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProfileHashUpdatedAt TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CandidateProfileVersion INTEGER NOT NULL DEFAULT 1", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsProfilePublic INTEGER NOT NULL DEFAULT 1", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsDefaultCandidate INTEGER NOT NULL DEFAULT 0", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedAt TEXT NOT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsDeleted INTEGER NOT NULL DEFAULT 0", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeletedAt TEXT NULL", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_UserAccounts_ProfileHash", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_UserAccounts_DefaultCandidate", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_UserRoles_UserId_Role", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_CandidateProfileInfos_UserId_InfoType_Version", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_CandidateProfileInfos_UserId_InfoType_IsCurrent", initialSchemaSql, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("AvatarEmoji", profileMigrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE UserAccounts", profileMigrationSql, StringComparison.OrdinalIgnoreCase);

        var auditMigrationPath = Path.Combine(migrationsDirectory, "003-add-audit-and-user-soft-delete-fields.sql");
        Assert.True(File.Exists(auditMigrationPath), "Audit and user soft delete SQL migration script is required.");
        var auditMigrationSql = File.ReadAllText(auditMigrationPath);

        Assert.Contains("UPDATE UserAccounts", auditMigrationSql, StringComparison.OrdinalIgnoreCase);
        foreach (var migrationSqlFile in migrationSqlFiles)
        {
            var migrationSql = File.ReadAllText(migrationSqlFile);
            Assert.DoesNotContain("ADD COLUMN", migrationSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BEGIN TRANSACTION", migrationSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COMMIT;", migrationSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Api_persists_candidate_profile_sharing_roles_and_versioned_infos()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var entityDirectory = Path.Combine(apiDirectory, "Infrastructure", "Data", "Entities");
        var initialSchemaPath = Path.Combine(apiDirectory, "Infrastructure", "Data", "Migrations", "001-initial-schema.sql");
        var migrationPath = Path.Combine(apiDirectory, "Infrastructure", "Data", "Migrations", "004-add-candidate-profile-sharing.sql");

        var userAccountText = File.ReadAllText(Path.Combine(entityDirectory, "UserAccount.cs"));
        var userRoleText = File.ReadAllText(Path.Combine(entityDirectory, "UserRoleAssignment.cs"));
        var profileInfoText = File.ReadAllText(Path.Combine(entityDirectory, "CandidateProfileInfo.cs"));
        var dbContextText = File.ReadAllText(Path.Combine(apiDirectory, "Infrastructure", "Data", "BioTwinApiDbContext.cs"));
        var programText = File.ReadAllText(Path.Combine(apiDirectory, "Program.cs"));
        var initialSchemaSql = File.ReadAllText(initialSchemaPath);
        var migrationSql = File.ReadAllText(migrationPath);

        Assert.Contains("ProfileHash", userAccountText, StringComparison.Ordinal);
        Assert.Contains("ProfileHashUpdatedAt", userAccountText, StringComparison.Ordinal);
        Assert.Contains("CandidateProfileVersion", userAccountText, StringComparison.Ordinal);
        Assert.Contains("IsProfilePublic", userAccountText, StringComparison.Ordinal);
        Assert.Contains("IsDefaultCandidate", userAccountText, StringComparison.Ordinal);
        Assert.Contains("List<UserRoleAssignment>", userAccountText, StringComparison.Ordinal);
        Assert.Contains("List<CandidateProfileInfo>", userAccountText, StringComparison.Ordinal);

        Assert.Contains("public sealed class UserRoleAssignment", userRoleText, StringComparison.Ordinal);
        Assert.Contains("public sealed class CandidateProfileInfo", profileInfoText, StringComparison.Ordinal);
        Assert.Contains("DbSet<UserRoleAssignment>", dbContextText, StringComparison.Ordinal);
        Assert.Contains("DbSet<CandidateProfileInfo>", dbContextText, StringComparison.Ordinal);
        Assert.Contains("HasIndex(role => new { role.UserId, role.Role }).IsUnique()", dbContextText, StringComparison.Ordinal);
        Assert.Contains("HasIndex(info => new { info.UserId, info.InfoType, info.Version }).IsUnique()", dbContextText, StringComparison.Ordinal);
        Assert.Contains("IX_UserAccounts_DefaultCandidate", dbContextText, StringComparison.Ordinal);
        Assert.Contains("DatabaseSchemaValidator.ValidateAsync", programText, StringComparison.Ordinal);

        Assert.Contains("ProfileHash TEXT", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE CandidateProfileInfos", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE UserRoles", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS UserRoles", migrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS CandidateProfileInfos", migrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_UserAccounts_ProfileHash", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_UserAccounts_DefaultCandidate", initialSchemaSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProfileHash", migrationSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsDefaultCandidate", migrationSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Api_startup_validates_schema_without_mutating_database()
    {
        var apiDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.AspNetCoreApi"));
        var programText = File.ReadAllText(Path.Combine(apiDirectory, "Program.cs"));
        var validatorText = File.ReadAllText(Path.Combine(apiDirectory, "Infrastructure", "Data", "DatabaseSchemaValidator.cs"));

        Assert.Contains("DatabaseSchemaValidator.ValidateAsync", programText, StringComparison.Ordinal);
        Assert.Contains("Database schema is out of date", validatorText, StringComparison.Ordinal);

        foreach (var forbiddenText in new[]
        {
            "EnsureCreatedAsync",
            "ExecuteSqlRaw",
            "ExecuteSqlInterpolated",
            "ALTER TABLE",
            "CREATE TABLE",
            "INSERT OR IGNORE",
            "PRAGMA",
            "EnsureUserProfileColumnsAsync",
            "EnsureCandidateProfileSchemaAsync",
            "BackfillProfileHashesAsync",
            "EnsureColumnAsync"
        })
        {
            Assert.DoesNotContain(forbiddenText, programText, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var forbiddenMutation in new[]
        {
            "ExecuteSqlRaw",
            "ExecuteSqlInterpolated",
            "ALTER TABLE",
            "CREATE TABLE",
            "INSERT ",
            "UPDATE ",
            "DELETE ",
            "DROP TABLE",
            "EnsureCreatedAsync"
        })
        {
            Assert.DoesNotContain(forbiddenMutation, validatorText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
