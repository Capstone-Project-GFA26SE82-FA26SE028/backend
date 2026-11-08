using AI.API.Extensions;
using AI.API.GrpcServices;
using AI.BLL.Interfaces;
using AI.Common.Settings;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables().AddCommandLine(args);
builder.Services.AddAssistant(builder.Configuration);
if (args.Contains("--prepare-model", StringComparer.Ordinal))
{
    Environment.ExitCode = await AI.API.Setup.ModelSetup.Run(builder.Configuration);
    return;
}

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

if (AI.API.Setup.DevelopmentBootstrap.ShouldRun(builder.Environment.EnvironmentName,
    builder.Configuration.GetValue<bool>("DevelopmentBootstrap:Enabled"),
    Environment.GetEnvironmentVariable("AI_DEVELOPMENT_BOOTSTRAP_COMPLETED")))
{
    await AI.API.Setup.DevelopmentBootstrap.Run(builder.Configuration, builder.Environment.ContentRootPath);
}

var app = builder.Build();
app.MapGrpcService<ChatGrpcService>();
app.MapGrpcService<RecommendationGrpcService>();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (AI.API.Clients.OllamaModelClient model, IOptions<AISettings> settings, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.DependencyTimeoutSeconds));
    try
    {
        var status = await model.GetStatusAsync(timeout.Token);
        return Results.Json(status, statusCode: status.Ready ? 200 : 503);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(503);
    }
});
app.Run();
