using System.Diagnostics;
using System.Text.Json;
using LavaPlayground.Api.Lava;
using LavaPlayground.Api.Rock;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RockConnectionService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

// When packaged in the desktop app, exit if the host app goes away so we don't
// leave an orphaned server holding the port (covers force-quit and crashes).
if (int.TryParse(Environment.GetEnvironmentVariable("LAVA_HOST_PID"), out var hostPid))
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            try { Process.GetProcessById(hostPid).Dispose(); }
            catch { Environment.Exit(0); }
            await Task.Delay(1000);
        }
    });
}

var filters = LavaFilterRegistry.CreateDefault();

// Loaded from embedded resources so they work whether the app runs from a
// build output folder or as a single-file self-contained binary (desktop app).
var sampleContextJson = ReadEmbeddedJson("sample-context.json");
var sampleContextElement = JsonDocument.Parse(sampleContextJson).RootElement;

// The sample "database" that entity command tags ({% person %}, {% group %}, ...) query.
var entityDataJson = ReadEmbeddedJson("sample-entities.json");
var entityData = JsonObjectConverter.ToDictionary(JsonDocument.Parse(entityDataJson).RootElement)
    .ToDictionary(
        kv => kv.Key,
        kv => (kv.Value as List<object?>) ?? new List<object?>(),
        StringComparer.OrdinalIgnoreCase);

static string ReadEmbeddedJson(string fileName)
{
    var assembly = typeof(Program).Assembly;
    var resourceName = assembly.GetManifestResourceNames()
        .First(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

// Optional: auto-connect to a Rock server at startup so remote mode
// survives backend restarts. Set via environment variables:
//   ROCK_BASE_URL + ROCK_API_KEY  (or ROCK_USERNAME + ROCK_PASSWORD)
var autoRockUrl = Environment.GetEnvironmentVariable("ROCK_BASE_URL");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/sample-context", () => Results.Content(sampleContextJson, "application/json"));

app.MapGet("/api/filters", () => filters.All);

app.MapGet("/api/capabilities", LavaCapabilities.Describe);

app.MapPost("/api/render", (RenderRequest request) =>
{
    if (string.IsNullOrEmpty(request.Template))
    {
        return Results.Ok(new RenderResponse(string.Empty, 0, null));
    }

    // Start from the sample context, then merge any caller-provided values on top.
    var context = JsonObjectConverter.ToDictionary(sampleContextElement);
    if (request.Context is JsonElement provided && provided.ValueKind == JsonValueKind.Object)
    {
        foreach (var kv in JsonObjectConverter.ToDictionary(provided))
        {
            context[kv.Key] = kv.Value;
        }
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        var template = LavaTemplate.Parse(request.Template);
        var output = template.Render(new RenderContext(context, filters, entityData));
        stopwatch.Stop();
        return Results.Ok(new RenderResponse(output, stopwatch.Elapsed.TotalMilliseconds, null));
    }
    catch (LavaException ex)
    {
        stopwatch.Stop();
        return Results.Ok(new RenderResponse(null, stopwatch.Elapsed.TotalMilliseconds, ex.Message));
    }
});

if (!string.IsNullOrWhiteSpace(autoRockUrl))
{
    // Fire-and-forget so a slow Rock server doesn't block startup.
    _ = Task.Run(async () =>
    {
        try
        {
            var rock = app.Services.GetRequiredService<RockConnectionService>();
            await rock.ConnectAsync(new RockConnectRequest(
                autoRockUrl,
                Environment.GetEnvironmentVariable("ROCK_API_KEY"),
                Environment.GetEnvironmentVariable("ROCK_USERNAME"),
                Environment.GetEnvironmentVariable("ROCK_PASSWORD")));
            app.Logger.LogInformation("Auto-connected to Rock server {Url}", autoRockUrl);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning("Rock auto-connect failed: {Message}", ex.Message);
        }
    });
}

// ---------------------------------------------------------------------------
// Remote mode: render on a real Rock server via its REST API.
// ---------------------------------------------------------------------------

app.MapGet("/api/rock/status", (RockConnectionService rock) => rock.Status);

app.MapPost("/api/rock/connect", async (RockConnectRequest request, RockConnectionService rock) =>
{
    try
    {
        return Results.Ok(await rock.ConnectAsync(request));
    }
    catch (RockProxyException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/rock/disconnect", (RockConnectionService rock) =>
{
    rock.Disconnect();
    return Results.Ok(rock.Status);
});

app.MapPost("/api/rock/render", async (RenderRequest request, RockConnectionService rock) =>
{
    if (string.IsNullOrEmpty(request.Template))
    {
        return Results.Ok(new RenderResponse(string.Empty, 0, null));
    }
    var result = await rock.RenderAsync(request.Template);
    return Results.Ok(new RenderResponse(result.Output, result.ElapsedMs, result.Error));
});

app.Run();

public record RenderRequest(string? Template, JsonElement? Context);

public record RenderResponse(string? Output, double ElapsedMs, string? Error);
