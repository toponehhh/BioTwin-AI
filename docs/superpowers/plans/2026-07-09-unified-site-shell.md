# Unified Site Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the floating navigation and inconsistent page widths with one responsive Obsidian Glass Header, a 1200px standard content axis, and a 1600px wide workspace axis.

**Architecture:** `MainLayout` owns the fixed Header, route-aware content-width selection, mobile menu, Admin menu, theme control, and global dialogs. `MainLayout.razor.css` owns shell geometry and responsive navigation; `app.css` owns shared tokens, panels, controls, page rhythm, and the Resume Workspace grid.

**Tech Stack:** .NET 10, Blazor WebAssembly, Razor components, CSS custom properties, xUnit source-contract tests.

## Global Constraints

- Preserve existing routes, authorization checks, API calls, theme state, authentication modal, and Admin menu close behavior.
- Standard content max width is `1200px`; Resume Workspace max width is approximately `1600px`.
- Header is fixed, full width, translucent, blurred, and aligned to the standard content axis.
- Remove the floating brand block, pill navigation, and hanging-lamp theme control.
- Use 8px or smaller panel radii unless an existing system control requires otherwise.
- Keep desktop, tablet, mobile, light theme, dark theme, keyboard focus, and reduced-motion behavior usable.
- Use `C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe` for build and test commands.

---

### Task 1: Lock the New Shell Contract with Tests

**Files:**
- Modify: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: existing source-file test helpers in `RouteScaffoldTests`.
- Produces: assertions for `site-header`, `site-header-inner`, `site-container`, `site-container-wide`, mobile navigation, and removed legacy controls.

- [ ] **Step 1: Replace legacy floating-header assertions with the new shell contract**

```csharp
[Fact]
public void Client_uses_fixed_obsidian_header_and_shared_content_axes()
{
    var layoutText = ReadLayout("MainLayout.razor");
    var layoutCss = ReadLayout("MainLayout.razor.css");

    Assert.Contains("site-header", layoutText, StringComparison.Ordinal);
    Assert.Contains("site-header-inner", layoutText, StringComparison.Ordinal);
    Assert.Contains("site-container", layoutText, StringComparison.Ordinal);
    Assert.Contains("site-container-wide", layoutText, StringComparison.Ordinal);
    Assert.Contains("mobile-menu-toggle", layoutText, StringComparison.Ordinal);
    Assert.Contains("position: fixed", layoutCss, StringComparison.Ordinal);
    Assert.Contains("max-width: 1200px", layoutCss, StringComparison.Ordinal);
    Assert.DoesNotContain("floating-menu", layoutText, StringComparison.Ordinal);
    Assert.DoesNotContain("lamp-theme-toggle", layoutText, StringComparison.Ordinal);
}

private static string ReadLayout(string fileName) => File.ReadAllText(Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BioTwin_AI.BlazorClient", "Layout", fileName)));
```

- [ ] **Step 2: Run the focused test and verify the legacy layout fails it**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false --filter "Client_uses_fixed_obsidian_header_and_shared_content_axes"
```

Expected: FAIL because `site-header` and shared container classes do not exist.

- [ ] **Step 3: Add route-width and mobile-menu close assertions**

```csharp
Assert.Contains("ContentContainerClass", layoutText, StringComparison.Ordinal);
Assert.Contains("/resume/workspace", layoutText, StringComparison.Ordinal);
Assert.Contains("CloseNavigationMenus", layoutText, StringComparison.Ordinal);
Assert.Contains("aria-expanded", layoutText, StringComparison.Ordinal);
```

- [ ] **Step 4: Run the full Blazor test project and record the expected legacy failures**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
```

Expected: legacy lamp, floating-menu, and full-bleed workspace assertions fail until Tasks 2-4 update implementation and tests.

- [ ] **Step 5: Commit the failing contract tests**

```powershell
git add tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "test: define unified site shell contract"
```

### Task 2: Replace MainLayout with the Fixed Header Shell

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/Layout/MainLayout.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Layout/MainLayout.razor.css`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: `ThemeState`, `SessionState`, `AuthModalState`, `NavigationManager`.
- Produces: `ContentContainerClass`, `ToggleMobileMenu`, `CloseNavigationMenus`, route-aware wide workspace selection.

- [ ] **Step 1: Inject navigation state and add route-aware shell state**

```razor
@inject NavigationManager Navigation
@using Microsoft.AspNetCore.Components.Routing

@code {
    private bool isMobileMenuOpen;
    private string ContentContainerClass => IsWideWorkspaceRoute()
        ? "site-container site-container-wide"
        : "site-container";

    private bool IsWideWorkspaceRoute()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].Trim('/');
        return string.Equals(path, "resume/workspace", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Replace the three floating controls with one semantic Header**

```razor
<header class="site-header">
    <div class="site-header-inner">
        <a class="site-brand" href="/" @onclick="CloseNavigationMenus">BioTwin <span>AI</span></a>
        <nav class="site-nav @(isMobileMenuOpen ? "open" : null)" aria-label="Primary navigation">
            <NavLink class="site-nav-link" href="" Match="NavLinkMatch.All" @onclick="CloseNavigationMenus">Home</NavLink>
            <NavLink class="site-nav-link" href="projects" @onclick="CloseNavigationMenus">Projects</NavLink>
            <NavLink class="site-nav-link" href="skills" @onclick="CloseNavigationMenus">Skills</NavLink>
        </nav>
        <div class="site-header-actions">
            <button class="theme-icon-button" title="@ThemeToggleLabel" aria-label="@ThemeToggleLabel" @onclick="ToggleTheme">◐</button>
            <button class="mobile-menu-toggle" aria-label="Toggle navigation" aria-expanded="@isMobileMenuOpen" @onclick="ToggleMobileMenu">☰</button>
            @* Existing sign-in/Admin branch remains here with current authorization behavior. *@
        </div>
    </div>
</header>
```

- [ ] **Step 3: Render the route-aware content container**

```razor
<main class="app-main">
    <article class="page-content @ContentContainerClass">
        <ErrorBoundary>
            <ChildContent>@Body</ChildContent>
            <ErrorContent Context="ex">@* retain existing error panel *@</ErrorContent>
        </ErrorBoundary>
    </article>
</main>
```

- [ ] **Step 4: Close navigation menus after route changes and clean up subscriptions**

```csharp
protected override async Task OnInitializedAsync()
{
    ThemeState.Changed += StateHasChanged;
    SessionState.Changed += StateHasChanged;
    Navigation.LocationChanged += OnLocationChanged;
    await ThemeState.InitializeAsync();
    await SessionState.RefreshAsync();
}

private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
    _ = InvokeAsync(() => { CloseNavigationMenus(); StateHasChanged(); });

private void CloseNavigationMenus()
{
    isAdminMenuOpen = false;
    isMobileMenuOpen = false;
}

public void Dispose()
{
    ThemeState.Changed -= StateHasChanged;
    SessionState.Changed -= StateHasChanged;
    Navigation.LocationChanged -= OnLocationChanged;
}
```

- [ ] **Step 5: Implement fixed Header geometry and responsive navigation**

```css
.site-header { position: fixed; inset: 0 0 auto; z-index: 60; min-height: 4.5rem; border-bottom: 1px solid var(--border); background: color-mix(in srgb, var(--bg) 72%, transparent); backdrop-filter: blur(24px) saturate(145%); }
.site-header-inner { width: min(100% - 4rem, 1200px); min-height: 4.5rem; margin: 0 auto; display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; }
.site-container { width: min(100% - 4rem, 1200px); margin-inline: auto; }
.site-container-wide { width: min(100% - 2rem, 1600px); max-width: 1600px; }
.app-main { padding-block: 7rem 3rem; }
@media (max-width: 760px) { .site-header-inner, .site-container, .site-container-wide { width: min(100% - 2rem, 1200px); } .site-nav { display: none; } .site-nav.open { display: grid; position: absolute; top: calc(100% + .5rem); inset-inline: 1rem; } }
```

- [ ] **Step 6: Run focused tests and commit the layout component**

Run the focused shell test; expected PASS.

```powershell
git add src/BioTwin_AI.BlazorClient/Layout/MainLayout.razor src/BioTwin_AI.BlazorClient/Layout/MainLayout.razor.css tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "feat: add unified fixed site header"
```

### Task 3: Consolidate Global Visual Tokens and Controls

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Modify: `src/BioTwin_AI.BlazorClient/Components/AuthModal.razor`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: shell classes from Task 2.
- Produces: shared panel/control classes with consistent radius, height, focus, and theme behavior.

- [ ] **Step 1: Add failing assertions for canonical tokens and removed lamp animation**

```csharp
Assert.Contains("--site-content-max: 1200px", appCss, StringComparison.Ordinal);
Assert.Contains("--site-workspace-max: 1600px", appCss, StringComparison.Ordinal);
Assert.Contains("--panel-radius: 8px", appCss, StringComparison.Ordinal);
Assert.Contains("--control-height: 2.875rem", appCss, StringComparison.Ordinal);
Assert.DoesNotContain("@keyframes lampCordPull", appCss, StringComparison.Ordinal);
```

- [ ] **Step 2: Replace decorative background and duplicated panel tokens with the shared system**

```css
:root { --site-content-max: 1200px; --site-workspace-max: 1600px; --site-gutter: 2rem; --panel-radius: 8px; --control-height: 2.875rem; }
body { background: var(--stitch-obsidian-lowest); }
.page { background: linear-gradient(180deg, rgba(192,193,255,.04), transparent 28rem), var(--bg); }
:where(.tool-panel, .glass-card, .mica-panel, .mac-window) { border: 1px solid var(--border); border-radius: var(--panel-radius); background: var(--glass-bg); box-shadow: var(--shadow); backdrop-filter: blur(var(--glass-blur)); }
:where(input, select, textarea, .command-button) { min-height: var(--control-height); font-size: .9375rem; line-height: 1.35; }
:where(button, a, input, select, textarea):focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) { *, *::before, *::after { scroll-behavior: auto !important; animation-duration: .01ms !important; transition-duration: .01ms !important; } }
```

- [ ] **Step 3: Remove obsolete `.floating-*`, lamp, perspective, and pill-navigation blocks**

Delete the CSS blocks rooted at `.floating-brand`, `.public-nav`, `.lamp-theme-toggle`, `.lamp-cord`, `.lamp-pull`, and their keyframes. Keep semantic Admin-menu, modal, timeline, and page-specific rules.

- [ ] **Step 4: Keep AuthModal markup but align its labels and action row to shared controls**

```razor
<section class="auth-modal auth-card mac-window" ...>
    @* retain existing fields and actions; remove redundant mica-panel styling hook *@
</section>
```

- [ ] **Step 5: Run Blazor tests and commit visual-token consolidation**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
git add src/BioTwin_AI.BlazorClient/wwwroot/css/app.css src/BioTwin_AI.BlazorClient/Components/AuthModal.razor tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "style: unify glass panels and controls"
```

### Task 4: Normalize Standard Pages and the Wide Resume Workspace

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Home.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Projects.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Skills.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Chat.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Settings.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/Resume.razor`
- Modify: `src/BioTwin_AI.BlazorClient/Pages/ResumeWorkspace.razor`
- Test: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: route-aware `site-container-wide` shell from Task 2.
- Produces: consistent page heading and section spacing; bounded three-column Resume Workspace.

- [ ] **Step 1: Replace the legacy full-bleed workspace test with bounded-wide assertions**

```csharp
Assert.Contains("grid-template-columns: minmax(220px, 280px) minmax(0, 1fr) minmax(220px, 280px)", appCss, StringComparison.Ordinal);
Assert.DoesNotContain("width: calc(100vw - 1rem)", appCss, StringComparison.Ordinal);
Assert.DoesNotContain("margin-left: calc(-50vw", appCss, StringComparison.Ordinal);
Assert.Contains("border-radius: var(--panel-radius)", appCss, StringComparison.Ordinal);
```

- [ ] **Step 2: Define one page rhythm and bounded workspace grid**

```css
.workspace-header { margin: 0 0 2rem; }
.workspace-header h1 { margin: 0 0 .65rem; font-size: clamp(2rem, 4vw, 3.25rem); }
.grid-section, .two-column { margin-top: 2rem; }
.resume-workspace { display: grid; grid-template-columns: minmax(220px, 280px) minmax(0, 1fr) minmax(220px, 280px); gap: 1rem; width: 100%; min-height: calc(100vh - 9rem); margin: 0; }
@media (max-width: 1180px) { .resume-workspace { grid-template-columns: minmax(220px, .7fr) minmax(0, 1.6fr); } .workspace-outline { grid-column: 1 / -1; } }
@media (max-width: 760px) { .resume-workspace { grid-template-columns: 1fr; } .workspace-outline { grid-column: auto; } }
```

- [ ] **Step 3: Remove page wrappers that set competing global widths**

Keep semantic page sections such as `workspace-header`, `grid-section`, and `resume-workspace`, but remove inline or page-specific outer width/margin classes. Do not change page data loading or event handlers.

- [ ] **Step 4: Run tests and the Blazor build**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build src\BioTwin_AI.BlazorClient\BioTwin_AI.BlazorClient.csproj -p:UseAppHost=false
```

Expected: all Blazor tests pass and build completes with zero errors.

- [ ] **Step 5: Commit normalized page layouts**

```powershell
git add src/BioTwin_AI.BlazorClient tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs
git commit -m "style: normalize page and workspace layouts"
```

### Task 5: Visual and Regression Verification

**Files:**
- Verify only; modify the preceding files only if a verified defect is found.

**Interfaces:**
- Consumes: completed shell and normalized pages.
- Produces: validated desktop, tablet, and mobile behavior.

- [ ] **Step 1: Run the complete Blazor test project and solution build**

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build BioTwin_AI.slnx -p:UseAppHost=false
```

Expected: all tests pass; build has zero errors.

- [ ] **Step 2: Start the client and API with repository launch settings**

Run the existing local startup workflow and note the actual client URL. Do not overwrite database schema or seed data from application startup.

- [ ] **Step 3: Capture desktop, tablet, and mobile screenshots**

Verify Home, Settings, Resume Library, and Resume Workspace at approximately `1440x900`, `1024x768`, and `390x844`. Check Header alignment, fixed positioning, menus, theme button, panel radii, no overflow, and usable editor dimensions.

- [ ] **Step 4: Verify Admin menu interactions**

Confirm it closes after selecting a route, clicking outside, and route completion; confirm public links and theme toggle remain usable in both themes.

- [ ] **Step 5: Commit any evidence-driven corrections**

```powershell
git add src/BioTwin_AI.BlazorClient tests/BioTwin_AI.BlazorClient.Tests
git commit -m "fix: polish unified responsive shell"
```

