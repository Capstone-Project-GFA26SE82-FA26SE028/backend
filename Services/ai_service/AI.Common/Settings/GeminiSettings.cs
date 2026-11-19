namespace AI.Common.Settings;

public sealed class GeminiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int MaximumOutputTokens { get; set; }
    public int MaximumPromptCharacters { get; set; }
    public int ReadinessTimeoutSeconds { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public bool RetryServiceUnavailable { get; set; }
    public int RetryDelayMilliseconds { get; set; }
}
