using System.Net.Http.Json;
using System.Text.Json;
using AI.Common.Settings;

namespace AI.API.Setup;

public static class ModelSetup
{
    public static async Task<int> Run(IConfiguration configuration, HttpMessageHandler? handler = null)
    {
        var settings = configuration.GetSection("Ollama").Get<OllamaSettings>()
            ?? throw new InvalidOperationException("Configure Ollama settings.");
        if (settings.Model != "qwen3:4b-instruct-2507-q4_K_M" || settings.ContextLength != 2048
            || !Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query))
        {
            Console.Error.WriteLine("The approved model, context or Ollama endpoint is not configured correctly.");
            return 1;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: handler is null)
        {
            BaseAddress = new Uri(endpoint.ToString().TrimEnd('/') + "/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        try
        {
            using var tags = await http.GetAsync("api/tags", timeout.Token);
            tags.EnsureSuccessStatusCode();
            using var installed = JsonDocument.Parse(await tags.Content.ReadAsStringAsync(timeout.Token));
            if (!installed.RootElement.GetProperty("models").EnumerateArray().Any(model => model.GetProperty("name").GetString() == settings.Model))
            {
                Console.WriteLine($"Downloading {settings.Model}; this is only needed when missing.");
                await Pull(http, settings.Model, timeout.Token);
            }
            else
            {
                Console.WriteLine("Approved model already installed; skipping download.");
            }

            using var show = await http.PostAsJsonAsync("api/show", new { model = settings.Model }, timeout.Token);
            show.EnsureSuccessStatusCode();
            using var metadata = JsonDocument.Parse(await show.Content.ReadAsStringAsync(timeout.Token));
            var details = metadata.RootElement.GetProperty("details");
            if (details.GetProperty("family").GetString() != "qwen3"
                || details.GetProperty("quantization_level").GetString() != "Q4_K_M"
                || details.GetProperty("parameter_size").GetString() != "4.0B")
            {
                throw new InvalidOperationException("Installed model metadata does not match the approved Qwen3 4B Q4_K_M. It was not overwritten.");
            }

            using var inference = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            inference.CancelAfter(TimeSpan.FromSeconds(90));
            using var smoke = await http.PostAsJsonAsync("api/generate", new
            {
                model = settings.Model,
                prompt = "Reply with the single word READY.",
                stream = false,
                keep_alive = $"{settings.KeepAliveMinutes}m",
                options = new
                {
                    num_ctx = settings.ContextLength,
                    num_predict = 32,
                    temperature = 0
                }
            }, inference.Token);
            smoke.EnsureSuccessStatusCode();
            using var answer = JsonDocument.Parse(await smoke.Content.ReadAsStringAsync(inference.Token));
            var root = answer.RootElement;
            if (!root.GetProperty("done").GetBoolean() || root.GetProperty("done_reason").GetString() != "stop"
                || !root.GetProperty("response").GetString()!.Trim().Equals("READY", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Model smoke test did not return the expected READY answer.");
            }

            using var running = await http.GetAsync("api/ps", inference.Token);
            running.EnsureSuccessStatusCode();
            using var loaded = JsonDocument.Parse(await running.Content.ReadAsStringAsync(inference.Token));
            if (!loaded.RootElement.GetProperty("models").EnumerateArray().Any(model =>
                model.GetProperty("name").GetString() == settings.Model
                && model.GetProperty("context_length").GetInt32() == settings.ContextLength))
            {
                throw new InvalidOperationException("Ollama did not retain the approved model with context 2048. Check runtime compatibility and memory, then rerun.");
            }

            Console.WriteLine("Model verified: qwen3:4b-instruct-2507-q4_K_M; context 2048; smoke test READY.");
            return 0;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
        {
            Console.Error.WriteLine($"AI setup failed: {error.Message} Check Ollama connectivity, installed model, available memory and disk space; then rerun setup.");
            return 1;
        }
    }

    private static async Task Pull(HttpClient http, string model, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var pull = await http.PostAsJsonAsync("api/pull", new { model, stream = false }, ct);
                var content = await pull.Content.ReadAsStringAsync(ct);
                if (pull.IsSuccessStatusCode)
                {
                    using var body = JsonDocument.Parse(content);
                    if (!body.RootElement.TryGetProperty("error", out _))
                    {
                        return;
                    }
                }

                if (attempt == 3 || ((int)pull.StatusCode < 500 && (int)pull.StatusCode != 429))
                {
                    var detail = content[..Math.Min(content.Length, 300)];
                    throw new InvalidOperationException($"Model download failed (HTTP {(int)pull.StatusCode}): {detail}. Check registry connectivity and Ollama logs; rerun to resume cached layers.");
                }
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                Console.WriteLine("Temporary registry connection failure; retrying the same model using cached layers.");
            }

            Console.WriteLine($"Retrying approved model download ({attempt + 1}/3); already downloaded layers are retained.");
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }
    }
}
