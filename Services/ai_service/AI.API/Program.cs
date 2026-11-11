using AI.API.Extensions;
using AI.API.GrpcServices;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables().AddCommandLine(args);
builder.Services.AddAssistant(builder.Configuration);
if (args.Contains("--check-ready", StringComparer.Ordinal))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    try
    {
        var health = new UriBuilder(builder.Configuration["Kestrel:Endpoints:Health:Url"]
            ?? throw new InvalidOperationException("Configure the AI health endpoint."));
        if (health.Host is "0.0.0.0" or "::" or "[::]")
        {
            health.Host = "localhost";
        }

        health.Path = "/health/ready";
        using var response = await client.GetAsync(health.Uri);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }

    return;
}

var app = builder.Build();
app.MapGrpcService<ChatGrpcService>();
app.MapGrpcService<RecommendationGrpcService>();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", (IOptions<GeminiSettings> settings) =>
{
    var configured = !string.IsNullOrWhiteSpace(settings.Value.ApiKey);
    return Results.Json(new
    {
        configured,
        provider = "Gemini",
        status = configured ? "ConfiguredNotProviderVerified" : "ApiKeyMissing"
    }, statusCode: configured ? 200 : 503);
});
app.Run();
