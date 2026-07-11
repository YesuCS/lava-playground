using System.Net;
using System.Text;
using System.Text.Json;

namespace LavaPlayground.Api.Rock;

public record RockConnectRequest(string? BaseUrl, string? ApiKey, string? Username, string? Password);

public record RockStatus(bool Connected, string? BaseUrl, string? AuthType);

public record RockRenderResult(string? Output, double ElapsedMs, string? Error);

/// <summary>
/// Holds a single active connection to a Rock RMS server and proxies Lava
/// rendering to Rock's <c>POST /api/Lava/RenderTemplate</c> endpoint, so the
/// browser never talks to Rock directly (no CORS issues, one origin).
///
/// Two auth styles are supported:
///   - REST API key → sent as the Authorization-Token header
///     (create one under Admin Tools → Security → REST Keys)
///   - Username/password → POST /api/Auth/Login, then the .ROCK cookie is
///     reused for subsequent calls
///
/// The credentials live only in this process's memory; nothing is written
/// to disk. This is a single-user dev tool by design.
/// </summary>
public class RockConnectionService
{
    private readonly object _lock = new();
    private HttpClient? _client;
    private string? _baseUrl;
    private string? _authType;

    public RockStatus Status
    {
        get
        {
            lock (_lock)
            {
                return new RockStatus(_client != null, _baseUrl, _authType);
            }
        }
    }

    public async Task<RockStatus> ConnectAsync(RockConnectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            throw new RockProxyException("A Rock server URL is required (e.g. https://rock.mychurch.org).");
        }

        var baseUrl = request.BaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            throw new RockProxyException("The Rock server URL must start with http:// or https://.");
        }

        var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        string authType;

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            client.DefaultRequestHeaders.Add("Authorization-Token", request.ApiKey.Trim());
            authType = "apiKey";
        }
        else if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var login = await client.PostAsync(
                $"{baseUrl}/api/Auth/Login",
                new StringContent(
                    JsonSerializer.Serialize(new { Username = request.Username, Password = request.Password, Persisted = true }),
                    Encoding.UTF8,
                    "application/json"));

            if (login.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new RockProxyException("Rock rejected that username/password.");
            }
            if (!login.IsSuccessStatusCode)
            {
                throw new RockProxyException($"Rock login failed with HTTP {(int)login.StatusCode}.");
            }
            authType = "login";
        }
        else
        {
            throw new RockProxyException("Provide either a REST API key or a username and password.");
        }

        // Prove we can actually render Lava before declaring success.
        var probe = await RenderOnRockAsync(client, baseUrl, "{{ 'connected' | Upcase }}");
        if (probe.Error != null)
        {
            throw new RockProxyException($"Connected to the server, but the Lava render check failed: {probe.Error}");
        }

        lock (_lock)
        {
            _client?.Dispose();
            _client = client;
            _baseUrl = baseUrl;
            _authType = authType;
        }
        return Status;
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
            _baseUrl = null;
            _authType = null;
        }
    }

    public async Task<RockRenderResult> RenderAsync(string template)
    {
        HttpClient client;
        string baseUrl;
        lock (_lock)
        {
            if (_client == null || _baseUrl == null)
            {
                return new RockRenderResult(null, 0, "Not connected to a Rock server. Open the connection settings first.");
            }
            client = _client;
            baseUrl = _baseUrl;
        }
        return await RenderOnRockAsync(client, baseUrl, template);
    }

    private static async Task<RockRenderResult> RenderOnRockAsync(HttpClient client, string baseUrl, string template)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Rock's endpoint takes the raw template as the request body.
            var response = await client.PostAsync(
                $"{baseUrl}/api/Lava/RenderTemplate",
                new StringContent(template, Encoding.UTF8, "text/plain"));
            stopwatch.Stop();

            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new RockRenderResult(null, stopwatch.Elapsed.TotalMilliseconds,
                    "Rock returned 401 Unauthorized. Check that the REST key or user has access to the Lava RenderTemplate endpoint.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return new RockRenderResult(null, stopwatch.Elapsed.TotalMilliseconds,
                    $"Rock returned HTTP {(int)response.StatusCode}: {Truncate(body, 500)}");
            }

            // Rock returns a JSON-encoded string.
            var output = body.StartsWith('"')
                ? JsonSerializer.Deserialize<string>(body) ?? string.Empty
                : body;
            return new RockRenderResult(output, stopwatch.Elapsed.TotalMilliseconds, null);
        }
        catch (TaskCanceledException)
        {
            return new RockRenderResult(null, stopwatch.Elapsed.TotalMilliseconds, "The request to the Rock server timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new RockRenderResult(null, stopwatch.Elapsed.TotalMilliseconds, $"Could not reach the Rock server: {ex.Message}");
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

public class RockProxyException : Exception
{
    public RockProxyException(string message) : base(message) { }
}
