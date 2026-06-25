namespace BioTwin_AI.BlazorClient.Tests.Architecture;

public class RouteScaffoldTests
{
    [Theory]
    [InlineData("Home.razor", "@page \"/\"")]
    [InlineData("Chat.razor", "@page \"/chat\"")]
    [InlineData("Resume.razor", "@page \"/resume\"")]
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
        var navText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "NavMenu.razor")));

        Assert.Contains("<AuthModal", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"login\"", navText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sign in", navText, StringComparison.OrdinalIgnoreCase);
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
    public void Header_uses_pull_cord_lamp_theme_toggle()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));
        var appCss = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "wwwroot", "css", "app.css")));

        Assert.Contains("lamp-theme-toggle", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-cord", layoutText, StringComparison.Ordinal);
        Assert.Contains("lamp-pull", layoutText, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ToggleTheme\"", layoutText, StringComparison.Ordinal);
        Assert.DoesNotContain("<button class=\"theme-toggle\" @onclick=\"ToggleTheme\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("@keyframes lampCordPull", appCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes lampShadeSwing", appCss, StringComparison.Ordinal);
        Assert.Contains(".lamp-theme-toggle.lamp-on", appCss, StringComparison.Ordinal);
        Assert.Contains("border: 0;", appCss, StringComparison.Ordinal);
        Assert.Contains("background: transparent;", appCss, StringComparison.Ordinal);
        Assert.Contains("height: 5.55rem;", appCss, StringComparison.Ordinal);
        Assert.Contains("top: 4.8rem;", appCss, StringComparison.Ordinal);
        Assert.Contains(".lamp-cord::after", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_remote_logger_sends_only_information_or_higher_without_http_recursion()
    {
        var loggingDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Services", "Logging"));
        var providerText = string.Join(Environment.NewLine, Directory.GetFiles(loggingDirectory, "*.cs").Select(File.ReadAllText));

        Assert.Contains("LogLevel.Information", providerText, StringComparison.Ordinal);
        Assert.Contains("logLevel < _minimumLevel", providerText, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http", providerText, StringComparison.Ordinal);
        Assert.Contains("ClientLogEntryRequest", providerText, StringComparison.Ordinal);
        Assert.Contains("PostAsJsonAsync", providerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_workspace_uses_second_level_navigation_for_distinct_workflows()
    {
        var navText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "NavMenu.razor")));

        Assert.Contains("resume-nav-group", navText, StringComparison.Ordinal);
        Assert.Contains("href=\"resume/upload\"", navText, StringComparison.Ordinal);
        Assert.Contains("href=\"resume/edit\"", navText, StringComparison.Ordinal);
        Assert.Contains("href=\"resume/export\"", navText, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigation_menu_refreshes_when_session_state_changes()
    {
        var navText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "NavMenu.razor")));

        Assert.Contains("@implements IDisposable", navText, StringComparison.Ordinal);
        Assert.Contains("SessionState.Changed +=", navText, StringComparison.Ordinal);
        Assert.Contains("SessionState.Changed -=", navText, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync(StateHasChanged)", navText, StringComparison.Ordinal);
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
    public void Auth_modal_collects_nickname_and_avatar_when_registering()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));

        Assert.Contains("@bind=\"nickname\"", modalText, StringComparison.Ordinal);
        Assert.Contains("avatar-options", modalText, StringComparison.Ordinal);
        Assert.Contains("Avatar:", modalText, StringComparison.Ordinal);
        Assert.DoesNotContain("AvatarEmoji", modalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_uses_avatar_profile_menu_for_authenticated_user_actions()
    {
        var layoutText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", "MainLayout.razor")));

        Assert.Contains("Welcome @SessionState.DisplayName", layoutText, StringComparison.Ordinal);
        Assert.Contains("profile-menu", layoutText, StringComparison.Ordinal);
        Assert.Contains("Edit profile", layoutText, StringComparison.Ordinal);
        Assert.Contains("href=\"/settings\"", layoutText, StringComparison.Ordinal);
        Assert.Contains("LogoutAsync", layoutText, StringComparison.Ordinal);
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
