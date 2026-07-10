namespace BioTwin_AI.BlazorClient.Tests.Architecture;

public class RouteScaffoldTests
{
    [Theory]
    [InlineData("Home.razor", "@page \"/\"")]
    [InlineData("Chat.razor", "@page \"/chat\"")]
    [InlineData("Resume.razor", "@page \"/resume\"")]
    [InlineData("ResumeCreate.razor", "@page \"/resume/create\"")]
    [InlineData("ResumeWorkspace.razor", "@page \"/resume/workspace\"")]
    [InlineData("ResumeUpload.razor", "@page \"/resume/upload\"")]
    [InlineData("ResumeEdit.razor", "@page \"/resume/edit/{ResumeId:int?}\"")]
    [InlineData("ResumeExport.razor", "@page \"/resume/export/{ResumeId:int?}\"")]
    [InlineData("Settings.razor", "@page \"/settings\"")]
    public void Client_declares_phase_one_routes(string fileName, string routeDirective)
    {
        var pageText = ReadPage(fileName);

        Assert.Contains(routeDirective, pageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_uses_global_auth_modal_instead_of_primary_login_page_navigation()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("<AuthModal", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"login\"", layoutText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_uses_fixed_obsidian_header_and_shared_content_axes()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));
        var layoutCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor.css")));

        Assert.Contains("site-header", layoutText, StringComparison.Ordinal);
        Assert.Contains("site-header-inner", layoutText, StringComparison.Ordinal);
        Assert.Contains("site-container", layoutText, StringComparison.Ordinal);
        Assert.Contains("site-container-wide", layoutText, StringComparison.Ordinal);
        Assert.Contains("mobile-menu-toggle", layoutText, StringComparison.Ordinal);
        Assert.Contains("ContentContainerClass", layoutText, StringComparison.Ordinal);
        Assert.Contains("CloseNavigationMenus", layoutText, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", layoutText, StringComparison.Ordinal);
        Assert.Contains("position: fixed", layoutCss, StringComparison.Ordinal);
        Assert.Contains("max-width: 1200px", layoutCss, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-menu", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-theme-toggle", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_logging_defaults_to_information_and_reports_startup_success()
    {
        var programText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Program.cs")));

        Assert.Contains("builder.Logging.SetMinimumLevel(LogLevel.Information)", programText, StringComparison.Ordinal);
        Assert.Contains("RemoteClientLoggerProvider", programText, StringComparison.Ordinal);
        Assert.Contains("api/client-logs", programText, StringComparison.Ordinal);
        Assert.Contains("started successfully", programText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LogInformation", programText, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_restores_accessible_pull_cord_lamp_as_the_rightmost_action()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));
        var layoutCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor.css")));

        Assert.Contains("lamp-theme-toggle", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-head", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-cord", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-pull", layoutText, StringComparison.Ordinal);
        Assert.Contains("ThemeToggleClass", layoutText, StringComparison.Ordinal);
        Assert.Contains("title=\"@ThemeToggleLabel\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@ThemeToggleLabel\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ToggleTheme\"", layoutText, StringComparison.Ordinal);
        Assert.Contains(".lamp-theme-toggle", layoutCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes lampCordPull", layoutCss, StringComparison.Ordinal);
        Assert.DoesNotContain("theme-icon-button", layoutText, StringComparison.Ordinal);

        var mobileMenuIndex = layoutText.IndexOf("mobile-menu-toggle", StringComparison.Ordinal);
        var lampIndex = layoutText.IndexOf("lamp-theme-toggle", StringComparison.Ordinal);
        Assert.True(mobileMenuIndex >= 0 && lampIndex > mobileMenuIndex, "The lamp must be the rightmost Header action.");
    }

    [Fact]
    public void Header_keeps_brand_and_theme_switch_at_the_edges_while_navigation_stays_centered()
    {
        var layoutCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor.css")));

        Assert.Contains("width: 100%;", layoutCss, StringComparison.Ordinal);
        Assert.Contains("max-width: none;", layoutCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);", layoutCss, StringComparison.Ordinal);
        Assert.Contains(".site-brand", layoutCss, StringComparison.Ordinal);
        Assert.Contains("justify-self: start;", layoutCss, StringComparison.Ordinal);
        Assert.Contains(".site-nav", layoutCss, StringComparison.Ordinal);
        Assert.Contains("justify-self: center;", layoutCss, StringComparison.Ordinal);
        Assert.Contains(".site-header-actions", layoutCss, StringComparison.Ordinal);
        Assert.Contains("justify-self: end;", layoutCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_remote_logger_uses_warning_plus_startup_information_and_current_page_provider()
    {
        var programText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Program.cs")));
        var loggingDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Services", "Logging"));
        var providerText = string.Join(Environment.NewLine, Directory.GetFiles(loggingDirectory, "*.cs").Select(File.ReadAllText));

        Assert.Contains("LogLevel.Warning", programText, StringComparison.Ordinal);
        Assert.Contains("LogLevel.Information", providerText, StringComparison.Ordinal);
        Assert.Contains("logLevel >= _minimumLevel", providerText, StringComparison.Ordinal);
        Assert.Contains("logLevel == LogLevel.Information", providerText, StringComparison.Ordinal);
        Assert.Contains("StartupCategory", providerText, StringComparison.Ordinal);
        Assert.Contains("currentPageProvider", providerText, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<NavigationManager>().Uri", programText, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http", providerText, StringComparison.Ordinal);
        Assert.Contains("ClientLogEntryRequest", providerText, StringComparison.Ordinal);
        Assert.Contains("PostAsJsonAsync", providerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_root_logs_unhandled_component_errors()
    {
        var clientRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient"));
        var appText = File.ReadAllText(Path.Combine(clientRoot, "App.razor"));
        var boundaryPath = Path.Combine(clientRoot, "Components", "ClientErrorBoundary.cs");
        var layoutText = File.ReadAllText(Path.Combine(clientRoot, "Layout", "MainLayout.razor"));

        Assert.True(File.Exists(boundaryPath));
        var boundaryText = File.ReadAllText(boundaryPath);

        Assert.Contains("<ClientErrorBoundary>", appText, StringComparison.Ordinal);
        Assert.Contains("<ErrorContent", appText, StringComparison.Ordinal);
        Assert.Contains("OnErrorAsync", boundaryText, StringComparison.Ordinal);
        Assert.Contains("LogError", boundaryText, StringComparison.Ordinal);
        Assert.Contains("LocationChanged +=", boundaryText, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync(Recover)", boundaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("<ErrorBoundary>", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticated_workspace_routes_live_under_the_admin_menu()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("Admin", layoutText, StringComparison.Ordinal);
        Assert.Contains("admin-menu", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/chat\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/resume/create\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/resume\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/resume/edit\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/resume/export\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("OpenProfileEditor", layoutText, StringComparison.Ordinal);
        Assert.Contains("LogoutAsync", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_project_does_not_duplicate_sdk_injected_hot_reload_package()
    {
        var projectText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "BioTwin_AI.BlazorClient.csproj")));

        Assert.DoesNotContain("Microsoft.DotNet.HotReload.WebAssembly.Browser", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_edit_renders_section_title_as_explicit_expression()
    {
        var pageText = ReadPage("ResumeEdit.razor");

        Assert.Contains("@(section.Title)", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("@section.Title", pageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_workspace_exposes_language_aware_editor_library_and_outline()
    {
        var pageText = ReadPage("ResumeWorkspace.razor");
        var editorText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "MarkdownEditor.razor")));
        var editorScriptPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "js", "markdownEditor.js"));
        var indexText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "index.html")));

        Assert.Contains("<ProtectedArea", pageText, StringComparison.Ordinal);
        Assert.Contains("resume-workspace", pageText, StringComparison.Ordinal);
        Assert.Contains("workspace-library", pageText, StringComparison.Ordinal);
        Assert.Contains("workspace-editor", pageText, StringComparison.Ordinal);
        Assert.Contains("workspace-outline", pageText, StringComparison.Ordinal);
        Assert.Contains("ResumeLanguages.SimplifiedChinese", pageText, StringComparison.Ordinal);
        Assert.Contains("ResumeLanguages.English", pageText, StringComparison.Ordinal);
        Assert.Contains("StartWorkspaceImportAsync", pageText, StringComparison.Ordinal);
        Assert.Contains("<MarkdownEditor", pageText, StringComparison.Ordinal);
        Assert.Contains("markdownEditor", editorText, StringComparison.Ordinal);
        Assert.True(File.Exists(editorScriptPath));
        Assert.Contains("EasyMDE", indexText, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_layout_refreshes_when_session_state_changes()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("@implements IDisposable", layoutText, StringComparison.Ordinal);
        Assert.Contains("SessionState.Changed +=", layoutText, StringComparison.Ordinal);
        Assert.Contains("SessionState.Changed -=", layoutText, StringComparison.Ordinal);
        Assert.Contains("StateHasChanged", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_library_page_does_not_mix_upload_edit_or_export_actions()
    {
        var pageText = ReadPage("Resume.razor");

        Assert.DoesNotContain("href=\"/resume/upload\"", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("/resume/edit/", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("/resume/export/", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload resume", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Edit<", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Export<", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auth_modal_uses_modal_dialog_semantics()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));

        Assert.Contains("role=\"dialog\"", modalText, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", modalText, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"auth-modal-title\"", modalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_modal_uses_stitch_mac_window_glass_treatment()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains("auth-window", modalText, StringComparison.Ordinal);
        Assert.Contains("window-controls", modalText, StringComparison.Ordinal);
        Assert.Contains("dot-red", modalText, StringComparison.Ordinal);
        Assert.Contains("dot-yellow", modalText, StringComparison.Ordinal);
        Assert.Contains("dot-green", modalText, StringComparison.Ordinal);
        Assert.Contains(".mac-window", appCss, StringComparison.Ordinal);
        Assert.Contains("backdrop-filter: blur(var(--glass-blur))", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_modal_collects_nickname_and_avatar_when_registering()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));

        Assert.Contains("@bind=\"nickname\"", modalText, StringComparison.Ordinal);
        Assert.Contains("avatar-options", modalText, StringComparison.Ordinal);
        Assert.Contains("compact-avatar-options", modalText, StringComparison.Ordinal);
        Assert.Contains("Avatar:", modalText, StringComparison.Ordinal);
        Assert.DoesNotContain("AvatarEmoji", modalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_modal_uses_balanced_glass_form_controls()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains("auth-card", modalText, StringComparison.Ordinal);
        Assert.Contains("auth-field", modalText, StringComparison.Ordinal);
        Assert.Contains("auth-input", modalText, StringComparison.Ordinal);
        Assert.Contains("auth-action-row", modalText, StringComparison.Ordinal);
        Assert.Contains("auth-button", modalText, StringComparison.Ordinal);
        Assert.Contains("min-height: 3rem;", appCss, StringComparison.Ordinal);
        Assert.Contains(".auth-action-row", appCss, StringComparison.Ordinal);
        Assert.Contains(".auth-button", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Primary_buttons_use_stitch_body_bold_centered_typography()
    {
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains("--button-font-size: 1rem", appCss, StringComparison.Ordinal);
        Assert.Contains("--button-line-height: 1.625rem", appCss, StringComparison.Ordinal);
        Assert.Contains("--button-font-weight: 600", appCss, StringComparison.Ordinal);
        Assert.Contains("align-items: center;", appCss, StringComparison.Ordinal);
        Assert.Contains("justify-content: center;", appCss, StringComparison.Ordinal);
        Assert.Contains("font-size: var(--button-font-size);", appCss, StringComparison.Ordinal);
        Assert.Contains("line-height: var(--button-line-height);", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_renders_public_candidate_profile_from_uid_query_string()
    {
        var programText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Program.cs")));
        var homeText = ReadPage("Home.razor");

        Assert.Contains("IPublicProfileApiClient", programText, StringComparison.Ordinal);
        Assert.Contains("PublicProfileApiClient", programText, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery(Name = \"uid\")", homeText, StringComparison.Ordinal);
        Assert.Contains("GetCandidateProfileAsync", homeText, StringComparison.Ordinal);
        Assert.Contains("career-timeline", homeText, StringComparison.Ordinal);
        Assert.Contains("work-timeline", homeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_uses_admin_menu_for_authenticated_user_actions()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("Welcome @SessionState.DisplayName", layoutText, StringComparison.Ordinal);
        Assert.Contains("admin-menu profile-menu", layoutText, StringComparison.Ordinal);
        Assert.Contains("Profile", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("LogoutAsync", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_menu_links_close_the_menu_after_navigation()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("private void CloseAdminMenu()", layoutText, StringComparison.Ordinal);

        foreach (var href in new[]
        {
            "/chat",
            "/resume/create",
            "/resume/workspace",
            "/resume",
            "/resume/edit",
            "/resume/export",
            "/settings"
        })
        {
            Assert.Contains($"href=\"{href}\" @onclick=\"CloseAdminMenu\"", layoutText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Admin_menu_closes_when_clicking_outside_menu()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));
        var layoutCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor.css")));

        Assert.Contains("admin-menu-backdrop", layoutText, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"CloseAdminMenu\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("@onclick:stopPropagation", layoutText, StringComparison.Ordinal);
        Assert.Contains(".admin-menu-backdrop", layoutCss, StringComparison.Ordinal);
        Assert.Contains("position: fixed;", layoutCss, StringComparison.Ordinal);
        Assert.Contains("inset: 0;", layoutCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_workspace_uses_bounded_wide_responsive_layout()
    {
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains(".resume-workspace", appCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(220px, 280px) minmax(0, 1fr) minmax(220px, 280px);", appCss, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(100vh - 9rem);", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain("width: calc(100vw - 1rem);", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-left: calc(-50vw", appCss, StringComparison.Ordinal);
        Assert.Contains("border-radius: var(--panel-radius);", appCss, StringComparison.Ordinal);
        Assert.Contains(".workspace-library,", appCss, StringComparison.Ordinal);
        Assert.Contains(".workspace-editor,", appCss, StringComparison.Ordinal);
        Assert.Contains(".workspace-outline", appCss, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ResumeWizardStepper.razor")]
    [InlineData("ResumeWizardStartStep.razor")]
    [InlineData("ResumeWizardProfileStep.razor")]
    [InlineData("ResumeWizardSummaryStep.razor")]
    [InlineData("ResumeWizardExperienceStep.razor")]
    [InlineData("ResumeWizardEducationStep.razor")]
    [InlineData("ResumeWizardSkillsProjectsStep.razor")]
    [InlineData("ResumeWizardReviewStep.razor")]
    public void Resume_wizard_uses_focused_components(string fileName)
    {
        var componentPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient", "Components", "ResumeWizard", fileName));

        Assert.True(File.Exists(componentPath), $"{fileName} should be a focused resume wizard component.");
    }

    [Fact]
    public void Resume_creation_page_uses_api_owned_import_pipeline_and_final_save()
    {
        var pageText = ReadPage("ResumeCreate.razor");

        Assert.Contains("<NavigationLock", pageText, StringComparison.Ordinal);
        Assert.Contains("ResumeWizardStepper", pageText, StringComparison.Ordinal);
        Assert.Contains("StartWizardImportAsync", pageText, StringComparison.Ordinal);
        Assert.Contains("AcquireOperationAsync", pageText, StringComparison.Ordinal);
        Assert.Contains("SaveResumeAsync", pageText, StringComparison.Ordinal);
        Assert.Contains("ResumeWizardMarkdownBuilder.Build", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractWizardAsync", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("MergePreviewAsync", pageText, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", pageText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MarkdownEditor", pageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_creation_wizard_matches_the_approved_responsive_glass_layout()
    {
        var appCss = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains(".resume-wizard-panel", appCss, StringComparison.Ordinal);
        Assert.Contains(".wizard-stepper", appCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(7, minmax(0, 1fr));", appCss, StringComparison.Ordinal);
        Assert.Contains(".wizard-mobile-progress", appCss, StringComparison.Ordinal);
        Assert.Contains(".wizard-actions", appCss, StringComparison.Ordinal);
        Assert.Contains("position: sticky", appCss, StringComparison.Ordinal);
        Assert.Contains("border-radius: var(--panel-radius)", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_imports_confirm_then_poll_api_owned_jobs_with_horizontal_progress()
    {
        var createText = ReadPage("ResumeCreate.razor");
        var workspaceText = ReadPage("ResumeWorkspace.razor");
        var apiText = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient", "Services", "Api", "ResumeApiClient.cs")));
        var overlayPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient", "Components", "BlockingProgressOverlay.razor"));
        var appCss = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.True(File.Exists(overlayPath));
        var overlayText = File.ReadAllText(overlayPath);
        Assert.Contains("role=\"dialog\"", overlayText, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", overlayText, StringComparison.Ordinal);
        Assert.Contains("resume-import-stage-rail", overlayText, StringComparison.Ordinal);
        Assert.Contains("OnCancel", overlayText, StringComparison.Ordinal);
        Assert.Contains("OnRetry", overlayText, StringComparison.Ordinal);
        Assert.Contains("OnChooseAnother", overlayText, StringComparison.Ordinal);
        Assert.Contains("ProgressLabel", overlayText, StringComparison.Ordinal);
        Assert.Contains("Processing stopped", overlayText, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            }\n        </section>", overlayText, StringComparison.Ordinal);
        Assert.Contains("FocusAsync", overlayText, StringComparison.Ordinal);
        Assert.Contains("@onkeydown:preventDefault", overlayText, StringComparison.Ordinal);
        Assert.Contains("api/resumes/import-jobs", apiText, StringComparison.Ordinal);
        Assert.Contains("api/resumes/operations", apiText, StringComparison.Ordinal);
        Assert.DoesNotContain("All2MD", apiText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/convert/jobs", apiText, StringComparison.Ordinal);
        Assert.Contains("PendingResumeUpload", createText, StringComparison.Ordinal);
        Assert.Contains("StartWizardImportAsync", createText, StringComparison.Ordinal);
        Assert.Contains("PendingResumeUpload", workspaceText, StringComparison.Ordinal);
        Assert.Contains("StartWorkspaceImportAsync", workspaceText, StringComparison.Ordinal);
        Assert.Contains("await ReleaseOperationAsync();\n                return false;", createText, StringComparison.Ordinal);
        Assert.Contains("await EnsureOperationAsync();\n        await RunImportAsync();", createText, StringComparison.Ordinal);
        Assert.Contains("await ReleaseOperationAsync();\n                return;", workspaceText, StringComparison.Ordinal);
        Assert.Contains("await EnsureOperationAsync();\n        await RunWorkspaceImportAsync();", workspaceText, StringComparison.Ordinal);
        Assert.DoesNotContain("StartConversionJobAsync", createText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConversionJobAsync", createText, StringComparison.Ordinal);
        Assert.DoesNotContain("MergePreviewAsync", createText, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractWizardAsync", createText, StringComparison.Ordinal);
        Assert.DoesNotContain("StartConversionJobAsync", workspaceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConversionJobAsync", workspaceText, StringComparison.Ordinal);
        Assert.Contains("<NavigationLock", createText, StringComparison.Ordinal);
        Assert.Contains("state.IsDirty || isBusy", createText, StringComparison.Ordinal);
        Assert.Contains("<NavigationLock", workspaceText, StringComparison.Ordinal);
        Assert.Contains("PreventNavigationDuringImport", workspaceText, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertUploadAsync(", createText, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertUploadAsync(", workspaceText, StringComparison.Ordinal);
        Assert.Contains(".blocking-progress-overlay", appCss, StringComparison.Ordinal);
        Assert.Contains(".resume-import-stage-rail", appCss, StringComparison.Ordinal);
        Assert.Contains("position: fixed;", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_uses_fixed_header_without_sidebar_or_floating_controls()
    {
        var indexText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "index.html")));
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));
        var layoutCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor.css")));
        Assert.Contains("cdn.tailwindcss.com", indexText, StringComparison.Ordinal);
        Assert.Contains("preflight: false", indexText, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavMenu", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"sidebar\"", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain(".sidebar", layoutCss, StringComparison.Ordinal);
        Assert.Contains("site-header", layoutText, StringComparison.Ordinal);
        Assert.Contains("site-header-inner", layoutText, StringComparison.Ordinal);
        Assert.Contains("site-nav", layoutText, StringComparison.Ordinal);
        Assert.Contains("mobile-menu-toggle", layoutText, StringComparison.Ordinal);
        Assert.Contains("nav-home", layoutText, StringComparison.Ordinal);
        Assert.Contains("nav-projects", layoutText, StringComparison.Ordinal);
        Assert.Contains("nav-skills", layoutText, StringComparison.Ordinal);
        Assert.Contains(".site-header", layoutCss, StringComparison.Ordinal);
        Assert.Contains("backdrop-filter: blur(24px)", layoutCss, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-menu", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("floating-theme-control", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("ambient-mask", layoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_uses_stitch_obsidian_alabaster_glass_tokens()
    {
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));
        var homeText = ReadPage("Home.razor");

        Assert.Contains("--stitch-obsidian-bg: #0b1326", appCss, StringComparison.Ordinal);
        Assert.Contains("--stitch-alabaster-bg: #f9f9ff", appCss, StringComparison.Ordinal);
        Assert.Contains("--glass-blur: 32px", appCss, StringComparison.Ordinal);
        Assert.Contains("--site-content-max: 1200px", appCss, StringComparison.Ordinal);
        Assert.Contains("--site-workspace-max: 1600px", appCss, StringComparison.Ordinal);
        Assert.Contains("--panel-radius: 8px", appCss, StringComparison.Ordinal);
        Assert.Contains("--control-height: 2.875rem", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain("@keyframes lampCordPull", appCss, StringComparison.Ordinal);
        Assert.Contains(".glass-card", appCss, StringComparison.Ordinal);
        Assert.Contains(".system-dot", appCss, StringComparison.Ordinal);
        Assert.Contains("artifact-hero", homeText, StringComparison.Ordinal);
        Assert.Contains("terminal-window", homeText, StringComparison.Ordinal);
        Assert.Contains("window-controls", homeText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Skills.razor", "RAG Systems")]
    [InlineData("Skills.razor", "Cloudflare Migration")]
    [InlineData("Projects.razor", "Resume Intelligence Workspace")]
    [InlineData("Projects.razor", "BioTwin AI Cloudflare Migration")]
    public void Public_profile_pages_do_not_show_unconfirmed_generated_content(string fileName, string generatedText)
    {
        var pageText = ReadPage(fileName);

        Assert.DoesNotContain(generatedText, pageText, StringComparison.Ordinal);
        Assert.Contains("user-confirmed content", pageText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Chat.razor")]
    [InlineData("Resume.razor")]
    [InlineData("ResumeCreate.razor")]
    [InlineData("ResumeWorkspace.razor")]
    [InlineData("ResumeUpload.razor")]
    [InlineData("ResumeEdit.razor")]
    [InlineData("ResumeExport.razor")]
    public void Backend_workspace_pages_are_protected_in_the_client(string fileName)
    {
        var pageText = ReadPage(fileName);

        Assert.Contains("<ProtectedArea", pageText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Counter.razor")]
    [InlineData("Weather.razor")]
    public void Client_removes_template_sample_pages(string fileName)
    {
        var pagePath = GetPagePath(fileName);

        Assert.False(File.Exists(pagePath), $"{fileName} should not remain in the M1 client scaffold.");
    }

    private static string ReadPage(string fileName)
    {
        return File.ReadAllText(GetPagePath(fileName));
    }

    private static string GetPagePath(string fileName)
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Pages", fileName));
    }
}
