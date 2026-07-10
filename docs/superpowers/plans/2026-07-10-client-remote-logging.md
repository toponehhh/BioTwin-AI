# Client Remote Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reliably forward important Blazor client failures through the BioTwin API into Serilog without leaking resume data, credentials, or URL query parameters.

**Architecture:** Strengthen the existing `RemoteClientLoggerProvider` instead of adding another logging stack. Centralize HTTP failure logging in `ApiClientBase`, capture unhandled component failures with a root `ClientErrorBoundary`, and harden `ClientLogsController` with matching severity and size policies.

**Tech Stack:** .NET 10, Blazor WebAssembly, `Microsoft.Extensions.Logging`, ASP.NET Core Web API, Serilog, xUnit.

## Global Constraints

- Upload `Warning`, `Error`, and `Critical`; upload exactly the successful startup `Information` exception; never upload ordinary `Information`, `Debug`, or `Trace`.
- Never send request or response bodies, cookies, authorization headers, passwords, resume Markdown, uploaded file bytes, profile hashes, query strings, or fragments.
- Log delivery is best-effort: no retries, no UI errors, and no recursive logging.
- Keep the existing `POST /api/client-logs` API boundary and do not call third-party logging services from the client.
- Do not add a database table or schema migration.
- Do not commit or push any change unless the user explicitly asks later.
- Use `C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe` for build and test verification.
- Shut down API, client, All2MD, and .NET build servers after verification.

---

### Task 1: Harden the Remote Logger

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/Services/Logging/RemoteClientLogger.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Logging/RemoteClientLoggerProvider.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Program.cs`
- Create: `tests/BioTwin_AI.BlazorClient.Tests/Logging/RemoteClientLoggerTests.cs`

**Interfaces:**
- Consumes: existing `ClientLogEntryRequest` and `POST /api/client-logs`.
- Produces: `RemoteClientLogger(HttpClient, string, string, Func<string?>, LogLevel)` and provider-created loggers with consistent filtering and redaction.

- [ ] **Step 1: Write failing behavior tests**

Create a recording `HttpMessageHandler` and verify:

```csharp
[Fact]
public async Task Sends_warning_with_path_but_without_query_string()
{
    var handler = new RecordingHandler();
    var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
    var logger = new RemoteClientLogger(
        client,
        "api/client-logs",
        "BioTwin_AI.BlazorClient.Pages.ResumeCreate",
        () => "https://client.example/resume/create?uid=secret#review",
        LogLevel.Warning);

    logger.LogWarning("Import failed for job {JobId}", "job-1");
    await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var entry = await handler.ReadEntryAsync();
    Assert.Equal("/resume/create", entry.Url);
    Assert.DoesNotContain("secret", handler.Body, StringComparison.Ordinal);
}
```

Also test that ordinary Information and HTTP infrastructure categories do not send, the startup Information category does send, and oversized message/exception strings are truncated.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj --filter "FullyQualifiedName~RemoteClientLoggerTests" -p:UseAppHost=false
```

Expected: FAIL because the logger does not accept a current-page provider, still uploads ordinary Information, and does not truncate payloads.

- [ ] **Step 3: Implement filtering, sanitization, and bounded payloads**

Implement constants and policy in `RemoteClientLogger`:

```csharp
private const int MaxCategoryLength = 256;
private const int MaxMessageLength = 2048;
private const int MaxExceptionLength = 8192;
private const string StartupCategory = "BioTwin_AI.BlazorClient.Startup";

public bool IsEnabled(LogLevel level) =>
    !IsRemoteLoggingInfrastructureCategory(_categoryName)
    && (level >= _minimumLevel
        || (level == LogLevel.Information
            && string.Equals(_categoryName, StartupCategory, StringComparison.Ordinal)));
```

Normalize the current browser URL with `Uri.AbsolutePath`, truncate all strings before constructing `ClientLogEntryRequest`, and retain the existing fire-and-forget `SendAsync` catch-all.

Register the provider through DI so it can read `NavigationManager.Uri`:

```csharp
builder.Services.AddSingleton<ILoggerProvider>(services =>
    new RemoteClientLoggerProvider(
        apiHttpClient,
        "api/client-logs",
        () => services.GetRequiredService<NavigationManager>().Uri,
        LogLevel.Warning));
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all `RemoteClientLoggerTests` pass.

- [ ] **Step 5: Review the task diff without committing**

Run `git diff --check` and inspect only the Task 1 files. Do not commit.

---

### Task 2: Log Shared API Failures Once

**Files:**
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ApiClientBase.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/AuthApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ChatApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/PublicProfileApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/ResumeApiClient.cs`
- Modify: `src/BioTwin_AI.BlazorClient/Services/Api/SettingsApiClient.cs`
- Create: `tests/BioTwin_AI.BlazorClient.Tests/Services/ApiClientBaseLoggingTests.cs`

**Interfaces:**
- Consumes: `ILogger<TApiClient>` supplied by Blazor DI.
- Produces: one warning for each non-success response or unexpected transport failure; explicit cancellation remains silent.

- [ ] **Step 1: Write failing API logging tests**

Expose a test subclass of `ApiClientBase` and a recording `ILogger`. Verify a `500` response logs method, path without query, and status exactly once; verify cancellation records no event.

```csharp
Assert.Contains("GET", log.Message, StringComparison.Ordinal);
Assert.Contains("/api/resumes/state", log.Message, StringComparison.Ordinal);
Assert.DoesNotContain("uid=secret", log.Message, StringComparison.Ordinal);
Assert.Equal(LogLevel.Warning, log.Level);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj --filter "FullyQualifiedName~ApiClientBaseLoggingTests" -p:UseAppHost=false
```

Expected: FAIL because `ApiClientBase` has no logger and does not record failures.

- [ ] **Step 3: Add one shared send boundary**

Change the base constructor to accept `ILogger`, and route all sends through one method:

```csharp
protected ApiClientBase(HttpClient httpClient, ILogger logger)
{
    HttpClient = httpClient;
    _logger = logger;
}

private async Task<HttpResponseMessage> SendLoggedAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    try
    {
        var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "BioTwin API returned {StatusCode} for {Method} {Path}",
                (int)response.StatusCode,
                request.Method.Method,
                SanitizePath(request.RequestUri));
        }

        return response;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        _logger.LogWarning(
            exception,
            "BioTwin API request failed for {Method} {Path}",
            request.Method.Method,
            SanitizePath(request.RequestUri));
        throw;
    }
}
```

Update each concrete API client constructor to accept its typed `ILogger<T>` and pass it to the base class. Do not log response bodies.

- [ ] **Step 4: Run focused and client tests**

Run the Step 2 command, then the complete `BioTwin_AI.BlazorClient.Tests` project. Expected: all pass.

- [ ] **Step 5: Review the task diff without committing**

Run `git diff --check`. Do not commit.

---

### Task 3: Capture Unhandled Component Exceptions

**Files:**
- Create: `src/BioTwin_AI.BlazorClient/Components/ClientErrorBoundary.cs`
- Modify: `src/BioTwin_AI.BlazorClient/App.razor`
- Modify: `src/BioTwin_AI.BlazorClient/wwwroot/css/app.css`
- Modify: `tests/BioTwin_AI.BlazorClient.Tests/Architecture/RouteScaffoldTests.cs`

**Interfaces:**
- Consumes: Blazor `ErrorBoundary`, `NavigationManager`, and `ILogger<ClientErrorBoundary>`.
- Produces: root-level unhandled exception logging and a recoverable, site-consistent error view.

- [ ] **Step 1: Write a failing architecture test**

Assert that `App.razor` wraps the router in `ClientErrorBoundary`, supplies `ErrorContent`, and that `ClientErrorBoundary` overrides `OnErrorAsync`, logs with `LogError`, subscribes to `LocationChanged`, and invokes `Recover` after navigation.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj --filter "FullyQualifiedName~RouteScaffoldTests.Client_root_logs_unhandled_component_errors" -p:UseAppHost=false
```

Expected: FAIL because the component does not exist.

- [ ] **Step 3: Implement the root error boundary**

Create a focused class:

```csharp
public sealed class ClientErrorBoundary : ErrorBoundary, IDisposable
{
    [Inject] public ILogger<ClientErrorBoundary> Logger { get; set; } = null!;
    [Inject] public NavigationManager Navigation { get; set; } = null!;

    protected override void OnInitialized() => Navigation.LocationChanged += HandleLocationChanged;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Unhandled Blazor component exception");
        return Task.CompletedTask;
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(Recover);

    public void Dispose() => Navigation.LocationChanged -= HandleLocationChanged;
}
```

Wrap the router in `App.razor` and provide concise reload/home recovery actions. Style the view with existing error and command-button tokens rather than introducing a separate visual system.

- [ ] **Step 4: Run the focused test and build the Blazor client**

Expected: the focused test passes and `BioTwin_AI.BlazorClient.csproj` builds with zero warnings and errors.

- [ ] **Step 5: Review the task diff without committing**

Run `git diff --check`. Do not commit.

---

### Task 4: Harden the Server Log Ingress and Verify End to End

**Files:**
- Modify: `src/BioTwin_AI.AspNetCoreApi/Controllers/ClientLogsController.cs`
- Create: `tests/BioTwin_AI.AspNetCoreApi.Tests/Controllers/ClientLogsControllerTests.cs`
- Modify: `tests/BioTwin_AI.AspNetCoreApi.Tests/Architecture/PhaseImplementationTests.cs`

**Interfaces:**
- Consumes: bounded `ClientLogEntryRequest` from the client.
- Produces: `202 Accepted` for every request and one structured Serilog event only for policy-approved levels.

- [ ] **Step 1: Write failing controller tests**

Use a recording `ILogger<ClientLogsController>` to verify ordinary Information and Debug requests produce no log; Warning requests produce one event; oversized fields are trimmed and bounded; query-bearing URLs are reduced to a path.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj --filter "FullyQualifiedName~ClientLogsControllerTests" -p:UseAppHost=false
```

Expected: FAIL because the controller currently accepts general Information logs and does not enforce bounds.

- [ ] **Step 3: Implement matching server policy**

Add fixed limits matching Task 1, retain `202 Accepted`, permit Information only for category `BioTwin_AI.BlazorClient.Startup`, sanitize URLs to `AbsolutePath`, and continue using structured template parameters. Never log a raw request object.

- [ ] **Step 4: Run all verification**

Run:

```powershell
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.AspNetCoreApi.Tests\BioTwin_AI.AspNetCoreApi.Tests.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests\BioTwin_AI.BlazorClient.Tests\BioTwin_AI.BlazorClient.Tests.csproj -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build src\BioTwin_AI.BlazorClient\BioTwin_AI.BlazorClient.csproj --no-restore -p:UseAppHost=false
& 'C:\Users\huangd\AppData\Local\Microsoft\dotnet\dotnet.exe' build BioTwin_AI.slnx --no-restore -p:UseAppHost=false
git diff --check
```

Expected: all tests pass and both builds complete with zero warnings and errors.

- [ ] **Step 5: Shut down verification services and report without committing**

Stop listeners on ports `5014`, `5193`, and `8000`, run `dotnet build-server shutdown`, confirm no related listeners remain, and report the changed files and verification evidence. Do not commit or push.
