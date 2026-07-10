using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace BioTwin_AI.BlazorClient.Services.Api;

public abstract class ApiClientBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger;
    protected readonly HttpClient HttpClient;

    protected ApiClientBase(HttpClient httpClient, ILogger logger)
    {
        HttpClient = httpClient;
        _logger = logger;
    }

    protected async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await SendLoggedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("API returned an empty response.");
    }

    protected async Task<T> SendJsonAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await SendLoggedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("API returned an empty response.");
    }

    protected async Task SendJsonAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await SendLoggedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    protected async Task<byte[]> GetBytesAsync(string uri, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await SendLoggedAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    protected HttpRequestMessage CreateCredentialedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return request;
    }

    protected async Task<HttpResponseMessage> SendLoggedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
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

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(BuildErrorMessage(response, body));
    }

    private static string BuildErrorMessage(HttpResponseMessage response, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"API request failed with status {(int)response.StatusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var title = root.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()
                : null;
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
            var recoveryHint = root.TryGetProperty("recoveryHint", out var hintElement)
                ? hintElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(message))
            {
                return string.IsNullOrWhiteSpace(recoveryHint)
                    ? message
                    : $"{message} {recoveryHint}";
            }

            if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Object)
            {
                var errors = new List<string>();
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            errors.Add($"{property.Name}: {item.GetString()}");
                        }
                    }
                }

                if (errors.Count > 0)
                {
                    return string.Join(" ", errors);
                }
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch (JsonException)
        {
            return body;
        }

        return body;
    }

    private static string SanitizePath(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return "/";
        }

        if (requestUri.IsAbsoluteUri)
        {
            return requestUri.AbsolutePath;
        }

        var path = requestUri.OriginalString;
        var suffixIndex = path.IndexOfAny(['?', '#']);
        if (suffixIndex >= 0)
        {
            path = path[..suffixIndex];
        }

        return path.StartsWith('/') ? path : $"/{path}";
    }
}
