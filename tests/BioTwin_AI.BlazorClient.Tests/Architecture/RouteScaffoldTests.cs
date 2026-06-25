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
    public void Auth_modal_uses_modal_dialog_semantics()
    {
        var modalText = File.ReadAllText(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Components", "AuthModal.razor")));

        Assert.Contains("role=\"dialog\"", modalText, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", modalText, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"auth-modal-title\"", modalText, StringComparison.Ordinal);
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
