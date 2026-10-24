using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.API.Setup;

namespace AI.Tests;

public sealed class ModelSetupTests
{
    [Fact]
    public async Task MissingModelIsPulledOnceAndRepeatedSetupReusesIt()
    {
        using var runtime = new SetupRuntime();
        var configuration = ConfigurationTests.Configuration();
        Assert.Equal(0, await ModelSetup.Run(configuration, runtime));
        Assert.Equal(1, runtime.PullCalls);
        Assert.Equal(0, await ModelSetup.Run(configuration, runtime));
        Assert.Equal(1, runtime.PullCalls);
        Assert.Equal(2, runtime.SmokeCalls);
    }

    [Fact]
    public async Task WrongQuantizationIsNotOverwrittenOrUsed()
    {
        using var runtime = new SetupRuntime
        {
            Installed = true,
            Quantization = "Q8_0"
        };
        Assert.Equal(1, await ModelSetup.Run(ConfigurationTests.Configuration(), runtime));
        Assert.Equal(0, runtime.PullCalls);
        Assert.Equal(0, runtime.SmokeCalls);
    }

    [Fact]
    public async Task TemporaryRegistryFailureRetriesTheSameModel()
    {
        using var runtime = new SetupRuntime { TemporaryPullFailure = true };
        Assert.Equal(0, await ModelSetup.Run(ConfigurationTests.Configuration(), runtime));
        Assert.Equal(2, runtime.PullCalls);
    }

    [Fact]
    public async Task RuntimeAloneDoesNotPassWarmupWhenLoadedContextIsWrong()
    {
        using var runtime = new SetupRuntime
        {
            Installed = true,
            ContextLength = 4096
        };
        Assert.Equal(1, await ModelSetup.Run(ConfigurationTests.Configuration(), runtime));
    }

    private sealed class SetupRuntime : HttpMessageHandler
    {
        public bool Installed { get; set; }
        public string Quantization { get; set; } = "Q4_K_M";
        public int ContextLength { get; set; } = 2048;
        public int PullCalls { get; private set; }
        public int SmokeCalls { get; private set; }
        public bool TemporaryPullFailure { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object result;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/tags":
                    result = new { models = Installed ? new[] { new { name = "qwen3:4b-instruct-2507-q4_K_M" } } : [] };
                    break;
                case "/api/pull":
                    PullCalls++;
                    if (TemporaryPullFailure && PullCalls == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = JsonContent.Create(new { error = "Temporary registry EOF" })
                        };
                    }

                    Installed = true;
                    result = new { status = "success" };
                    break;
                case "/api/show":
                    result = new { details = new { family = "qwen3", parameter_size = "4.0B", quantization_level = Quantization } };
                    break;
                case "/api/generate":
                    SmokeCalls++;
                    using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                    {
                        Assert.Equal("qwen3:4b-instruct-2507-q4_K_M", body.RootElement.GetProperty("model").GetString());
                        Assert.Equal(2048, body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
                    }

                    result = new { done = true, done_reason = "stop", response = "READY" };
                    break;
                case "/api/ps":
                    result = new { models = new[] { new { name = "qwen3:4b-instruct-2507-q4_K_M", context_length = ContextLength } } };
                    break;
                default:
                    throw new InvalidOperationException("Unexpected setup request.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(result)
            };
        }
    }
}
