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

var filters = LavaFilterRegistry.CreateDefault();

var sampleContextJson = File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "SampleData", "sample-context.json"));
var sampleContextElement = JsonDocument.Parse(sampleContextJson).RootElement;

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/sample-context", () => Results.Content(sampleContextJson, "application/json"));

app.MapGet("/api/filters", () => filters.All);

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
        var output = template.Render(new RenderContext(context, filters));
        stopwatch.Stop();
        return Results.Ok(new RenderResponse(output, stopwatch.Elapsed.TotalMilliseconds, null));
    }
    catch (LavaException ex)
    {
        stopwatch.Stop();
        return Results.Ok(new RenderResponse(null, stopwatch.Elapsed.TotalMilliseconds, ex.Message));
    }
});

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
